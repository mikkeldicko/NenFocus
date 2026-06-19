using Microsoft.Win32;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects; // Ensure this is here
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NenFocus.Infrastructure.Services;

public sealed class WindowsEnforcementService : IEnforcementService, IDisposable
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string BlockMarkerStart = "# NEN_FOCUS_START";
    private const string BlockMarkerEnd = "# NEN_FOCUS_END";

    private static readonly string[] BlockAddresses = { "0.0.0.0", "::" };

    private const int ProcessPollIntervalMs = 2000;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly System.Timers.Timer _processKillTimer;
    private readonly IBlockedAttemptTracker _attemptTracker;

    // FIX: Added missing private field and updated type
    private ActivePolicySlice? _currentPolicy;
    private HashSet<string> _blockedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isEnforcing;

    private HttpListener? _httpListener;
    private CancellationTokenSource? _serverCts;

    private readonly ConcurrentDictionary<string, bool> _trustedSignatureCache = new();
    private readonly int _currentPid = Environment.ProcessId;

    public WindowsEnforcementService(INotificationService notifications, IBlockedAttemptTracker attemptTracker)
    {
        _attemptTracker = attemptTracker;
        _processKillTimer = new System.Timers.Timer(ProcessPollIntervalMs);
        _processKillTimer.Elapsed += OnProcessKillTimerElapsed;
        _processKillTimer.AutoReset = true;

        EnsureRegistryPolicies();
        StartLocalServer();
    }

    // FIX: Updated return type to match interface
    public Task<ActivePolicySlice?> GetCurrentPolicyAsync(CancellationToken ct = default)
        => Task.FromResult(_currentPolicy);

    public Task<bool> IsActiveAsync(CancellationToken ct = default)
        => Task.FromResult(_isEnforcing);

    public async Task ApplyPolicyAsync(ActivePolicySlice policy, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _currentPolicy = policy;
            _isEnforcing = !policy.IsEmpty;

            await ApplyHostsRulesAsync(policy, ct);

            _blockedProcessNames = new HashSet<string>(
                policy.BlockedApps.Select(NormalizeProcessName),
                StringComparer.OrdinalIgnoreCase);

            if (_blockedProcessNames.Count > 0)
            {
                KillBlockedProcessesImmediate();
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
            _blockedProcessNames = new HashSet<string>();
            _currentPolicy = null;
            _isEnforcing = false;
            await RemoveHostsRulesAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // ... (Keep the rest of the private methods like KillBlockedProcessesImmediate, Normalize, etc. as they were) ...

    private void KillBlockedProcessesImmediate()
    {
        var processes = Process.GetProcesses();
        foreach (var p in processes)
        {
            try
            {
                if (p.Id == _currentPid) continue;
                string pName = p.ProcessName;
                if (SystemSafetyConstants.UnkillableProcesses.Contains(pName)) continue;
                if (!_blockedProcessNames.Contains(pName)) continue;

                string? filePath = GetProcessPath(p);
                if (string.IsNullOrEmpty(filePath) || IsSystemProtected(filePath)) continue;

                if (_currentPolicy?.ActiveSessionId != null)
                {
                    _ = _attemptTracker.RecordAppBlockedAsync(_currentPolicy.ActiveSessionId.Value, pName);
                }

                p.Kill(entireProcessTree: true);
            }
            catch { }
            finally { p.Dispose(); }
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
                return cert.Subject.Contains(SystemSafetyConstants.MicrosoftPublisher) ||
                       cert.Subject.Contains(SystemSafetyConstants.NenFocusPublisher);
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
            var domain = NormalizeDomain(site);
            if (string.IsNullOrWhiteSpace(domain)) continue;
            foreach (var addr in BlockAddresses)
            {
                sb.AppendLine($"{addr} {domain}");
                sb.AppendLine($"{addr} www.{domain}");
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

    private static string NormalizeProcessName(string app) { var trimmed = app.Trim(); return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed; }
    private static string NormalizeDomain(string site)
    {
        if (string.IsNullOrWhiteSpace(site)) return string.Empty;
        var s = site.Trim().ToLowerInvariant();
        if (s.Contains("://")) s = s.Split("://")[1];
        s = s.Split('/')[0].Split('?')[0].Split(':')[0];
        if (s.StartsWith("www.")) s = s[4..];
        return s;
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
                    _ = HandleRequestAsync(context); // This makes the method NOT greyed out
                }
                catch { /* Handle listener shutdown */ }
            }
        }, _serverCts.Token);
    }
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        // 1. ADD THIS: Mandatory CORS header for the extension to read the response
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
            var domain = context.Request.QueryString["domain"];
            bool isBlocked;

            if (_currentPolicy == null || !_isEnforcing || string.IsNullOrEmpty(domain))
            {
                isBlocked = false;
            }
            else if (_currentPolicy.WebsitePolicyMode == WebsitePolicy.Allowlist)
            {
                // Default-deny: block everything NOT in the allowed list
                isBlocked = !_currentPolicy.AllowedSites.Any(s =>
                    NormalizeDomain(s).Equals(domain, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                isBlocked = _currentPolicy.BlockedSites.Any(s =>
                    NormalizeDomain(s).Equals(domain, StringComparison.OrdinalIgnoreCase));
            }

            var json = JsonSerializer.Serialize(new { blocked = isBlocked });
            var buffer = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }

        if (url?.AbsolutePath == "/report")
        {
            var domain = context.Request.QueryString["domain"];
            var sessionId = _currentPolicy?.ActiveSessionId;
            if (!string.IsNullOrEmpty(domain) && sessionId.HasValue)
                _ = _attemptTracker.RecordWebBlockedAsync(sessionId.Value, domain);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
        }

        context.Response.Close();
    }
    public void Dispose()
    {
        _serverCts?.Cancel();
        _httpListener?.Stop();
        _processKillTimer.Stop();
        _processKillTimer.Dispose();
        _lock.Dispose();
    }

    private void OnProcessKillTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    => KillBlockedProcessesImmediate();
}