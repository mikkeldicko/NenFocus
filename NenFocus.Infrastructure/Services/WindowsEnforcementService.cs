using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.RecordBlockedAttempt;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NenFocus.Infrastructure.Services;

public sealed class WindowsEnforcementService : IEnforcementService, IDisposable
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string BlockMarkerStart = "# NEN_FOCUS_START";
    private const string BlockMarkerEnd = "# NEN_FOCUS_END";

    // Executables signed by these publishers are never killed.
    private const string MicrosoftPublisher = "O=Microsoft Corporation";
    private const string NenFocusPublisher = "PC-NenFocus-Developer";

    private static readonly string[] BlockAddresses = { "0.0.0.0", "::" };

    private const int ProcessPollIntervalMs = 2000;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly System.Timers.Timer _processKillTimer;
    private readonly ICommandDispatcher _commands;
    private readonly ISystemClock _clock;
    private readonly ILogger<WindowsEnforcementService> _logger;

    // Attempts are recorded one at a time, in the order seen (Gap 112, I3): the repeat check reads the
    // latest attempt before adding, so two reports of the same thing must never run side by side.
    private readonly Channel<RecordBlockedAttemptCommand> _attempts =
        Channel.CreateUnbounded<RecordBlockedAttemptCommand>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _attemptRecorder;

    private ActivePolicySlice? _currentPolicy;
    private bool _isEnforcing;

    private HttpListener? _httpListener;
    private CancellationTokenSource? _serverCts;

    private readonly ConcurrentDictionary<string, bool> _trustedSignatureCache = new();
    private readonly int _currentPid = Environment.ProcessId;

    public WindowsEnforcementService(
        ICommandDispatcher commands, ISystemClock clock, ILogger<WindowsEnforcementService> logger)
    {
        _commands = commands;
        _clock = clock;
        _logger = logger;
        _attemptRecorder = Task.Run(RecordAttemptsAsync);
        _processKillTimer = new System.Timers.Timer(ProcessPollIntervalMs);
        _processKillTimer.Elapsed += OnProcessKillTimerElapsed;
        _processKillTimer.AutoReset = true;

        EnsureRegistryPolicies();
        StartLocalServer();
    }

    public async Task ApplyPolicyAsync(ActivePolicySlice policy, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _currentPolicy = policy;
            _isEnforcing = !policy.IsEmpty;

            await ApplyHostsRulesAsync(policy, ct);

            if (policy.BlockedApps.Count > 0)
            {
                // This first sweep catches apps that were already running before/when the
                // block activated - the app equivalent of an already-open browser tab. That's
                // not an attempt to access something blocked, just something ambiently caught
                // by a new policy; the process-kill timer below is what catches a genuine
                // relaunch attempt made *during* the active block, which does count.
                KillBlockedProcessesImmediate(reportAttempts: false);
                _processKillTimer.Start();
            }
            else
            {
                _processKillTimer.Stop();
            }
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveEnforcementAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _processKillTimer.Stop();
            _currentPolicy = null;
            _isEnforcing = false;
            await RemoveHostsRulesAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private void KillBlockedProcessesImmediate(bool reportAttempts)
    {
        if (_currentPolicy is not { } policy) return;

        // One app is many processes (Chrome runs dozens named "chrome") — one launch is one report.
        var killed = new HashSet<BlockedApp>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == _currentPid) continue;
                // The slice answers which listed app this process is (Gap 112, C2); a critical
                // process can't be a BlockedApp, so it never matches.
                if (policy.BlockedAppFor(p.ProcessName) is not { } app) continue;

                string? filePath = GetProcessPath(p);
                if (string.IsNullOrEmpty(filePath) || IsSystemProtected(filePath)) continue;

                p.Kill(entireProcessTree: true);
                killed.Add(app);
            }
            catch { }
            finally { p.Dispose(); }
        }

        if (reportAttempts)
            foreach (var app in killed)
                Report(app.ProcessName, BlockedAttemptType.App, policy);
    }

    // Enforcement reports what it saw and under which policy; the use case decides what it belongs to.
    private void Report(string resource, BlockedAttemptType kind, ActivePolicySlice policy) =>
        _attempts.Writer.TryWrite(new RecordBlockedAttemptCommand
        {
            Resource = resource,
            Kind = kind,
            OccurredAt = _clock.Now,
            Policy = policy,
        });

    private async Task RecordAttemptsAsync()
    {
        await foreach (var attempt in _attempts.Reader.ReadAllAsync())
        {
            try { await _commands.DispatchAsync(attempt); }
            catch (Exception ex) { _logger.LogError(ex, "Could not record a blocked attempt at {Resource}", attempt.Resource); }
        }
    }

    private string? GetProcessPath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }

    private bool IsSystemProtected(string filePath)
    {
        if (filePath.Contains(@"C:\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains(@"C:\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase))
            return true;

        return _trustedSignatureCache.GetOrAdd(filePath, path => {
            try
            {
                using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
                return cert.Subject.Contains(MicrosoftPublisher) ||
                       cert.Subject.Contains(NenFocusPublisher);
            }
            catch { return false; }
        });
    }

    private async Task ApplyHostsRulesAsync(ActivePolicySlice policy, CancellationToken ct)
    {
        var hostsContent = File.Exists(HostsFilePath) ? await File.ReadAllTextAsync(HostsFilePath, ct) : "";
        hostsContent = StripNenFocusBlock(hostsContent);

        if (policy.IsEmpty)
        {
            await File.WriteAllTextAsync(HostsFilePath, hostsContent, new UTF8Encoding(false), ct);
            FlushDns();
            return;
        }

        var sb = new StringBuilder(hostsContent.TrimEnd());
        sb.AppendLine().AppendLine(BlockMarkerStart);
        foreach (var site in policy.BlockedSites)
        {
            // The hosts file can't wildcard subdomains — the extension covers those (Gap 112).
            foreach (var addr in BlockAddresses)
            {
                sb.AppendLine($"{addr} {site.Url}");
                sb.AppendLine($"{addr} www.{site.Url}");
            }
        }
        sb.AppendLine(BlockMarkerEnd);
        await File.WriteAllTextAsync(HostsFilePath, sb.ToString(), new UTF8Encoding(false), ct);
        FlushDns();
    }

    private async Task RemoveHostsRulesAsync(CancellationToken ct)
    {
        if (!File.Exists(HostsFilePath)) return;
        var content = await File.ReadAllTextAsync(HostsFilePath, ct);
        await File.WriteAllTextAsync(HostsFilePath, StripNenFocusBlock(content), new UTF8Encoding(false), ct);
        FlushDns();
    }

    private static void FlushDns() { try { Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true })?.WaitForExit(2000); } catch { } }

    private void EnsureRegistryPolicies()
    {
        string[] browserKeys = { @"SOFTWARE\Policies\BraveSoftware\Brave", @"SOFTWARE\Policies\Google\Chrome", @"SOFTWARE\Policies\Microsoft\Edge" };
        foreach (var keyPath in browserKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(keyPath);
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
            }
            catch { }
        }
    }

    private static string StripNenFocusBlock(string hostsContent)
    {
        var lines = hostsContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var result = new List<string>();
        var inBlock = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.Equals(trimmed, BlockMarkerStart, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
            if (string.Equals(trimmed, BlockMarkerEnd, StringComparison.OrdinalIgnoreCase)) { inBlock = false; continue; }
            if (!inBlock) result.Add(line);
        }
        return string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine;
    }

    private void StartLocalServer()
    {
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add("http://localhost:5005/");
        _httpListener.Start();

        _serverCts = new CancellationTokenSource();

        // Start the listening loop in a background task
        Task.Run(async () =>
        {
            while (!_serverCts.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = HandleRequestAsync(context);
                }
                catch { /* Handle listener shutdown */ }
            }
        }, _serverCts.Token);
    }
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        context.Response.AppendHeader("Access-Control-Allow-Origin", "*");
        context.Response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

        // Handle preflight OPTIONS requests (sometimes sent by browsers)
        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Close();
            return;
        }

        var url = context.Request.Url;
        if (url?.AbsolutePath == "/check")
        {
            // The slice decides (Gap 112, I2); a domain that isn't one is never blocked.
            var policy = _currentPolicy;
            var isBlocked = policy is not null && _isEnforcing
                && Website.TryCreate(context.Request.QueryString["domain"], out var site)
                && policy.Blocks(site);

            var json = JsonSerializer.Serialize(new { blocked = isBlocked });
            var buffer = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }

        if (url?.AbsolutePath == "/report")
        {
            var policy = _currentPolicy;
            if (policy is not null && Website.TryCreate(context.Request.QueryString["domain"], out var site))
                Report(site.Url, BlockedAttemptType.Website, policy);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
        }

        context.Response.Close();
    }
    public void Dispose()
    {
        _attempts.Writer.TryComplete();
        _serverCts?.Cancel();
        _httpListener?.Stop();
        _processKillTimer.Stop();
        _processKillTimer.Dispose();
        _lock.Dispose();
    }

    // Anything this ongoing loop catches is a relaunch attempted while the block was already
    // active - a genuine attempt, unlike ApplyPolicyAsync's initial sweep above.
    private void OnProcessKillTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    => KillBlockedProcessesImmediate(reportAttempts: true);
}