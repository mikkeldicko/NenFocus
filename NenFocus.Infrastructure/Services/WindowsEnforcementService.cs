using Microsoft.Win32;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;
using NenFocus.Domain.Enums;
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

    private static readonly string[] BlockAddresses = { "0.0.0.0", "::" }; // Changed to :: for better IPv6 blocking

    private const int ProcessPollIntervalMs = 2000;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly System.Timers.Timer _processKillTimer;
    private readonly IBlockedAttemptTracker _attemptTracker;

    private PolicySliceDto? _currentPolicy;
    private HashSet<string> _blockedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isEnforcing;

    // Server fields
    private HttpListener? _httpListener;
    private CancellationTokenSource? _serverCts;

    private readonly ConcurrentDictionary<string, bool> _trustedSignatureCache = new();
    private readonly int _currentPid = Environment.ProcessId;

    public WindowsEnforcementService(INotificationService notifications, IBlockedAttemptTracker attemptTracker)
    {
        _attemptTracker = attemptTracker;

        // Timer for App Blocking (Not browsers)
        _processKillTimer = new System.Timers.Timer(ProcessPollIntervalMs);
        _processKillTimer.Elapsed += OnProcessKillTimerElapsed;
        _processKillTimer.AutoReset = true;

        EnsureRegistryPolicies();
        StartLocalServer(); // Start the bridge for the extension
    }

    public Task<PolicySliceDto?> GetCurrentPolicyAsync(CancellationToken ct = default)
        => Task.FromResult(_currentPolicy);

    public Task<bool> IsActiveAsync(CancellationToken ct = default)
        => Task.FromResult(_isEnforcing);

    public async Task ApplyPolicyAsync(PolicySliceDto policy, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _currentPolicy = policy;
            _isEnforcing = !policy.IsEmpty;

            // 1. Update Hosts File (The backup hard-block)
            await ApplyHostsRulesAsync(policy, ct);

            // 2. Update App Blocking State
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
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveEnforcementAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _processKillTimer.Stop();
            _blockedProcessNames = [];
            _currentPolicy = null;
            _isEnforcing = false;
            await RemoveHostsRulesAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // --- Local API for Browser Extension ---

    private void StartLocalServer()
    {
        if (_httpListener != null && _httpListener.IsListening) return;

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add("http://localhost:5005/");
        _httpListener.Start();
        _serverCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (!_serverCts.IsCancellationRequested && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch { }
            }
        });
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.Close();
            return;
        }

        try
        {
            var path = request.Url?.AbsolutePath;

            if (path == "/check")
            {
                var domain = request.QueryString["domain"]?.ToLower();
                bool isBlocked = false;

                if (_isEnforcing && _currentPolicy != null && !string.IsNullOrEmpty(domain))
                {
                    var normalizedInput = NormalizeDomain(domain);

                    if (_currentPolicy.WebsitePolicyMode == WebsitePolicy.Allowlist)
                    {
                        bool isAllowed = _currentPolicy.AllowedSites.Any(s => NormalizeDomain(s) == normalizedInput);
                        isBlocked = !isAllowed;
                    }
                    else
                    {
                        isBlocked = _currentPolicy.BlockedSites.Any(s => NormalizeDomain(s) == normalizedInput);
                    }
                }

                var json = JsonSerializer.Serialize(new { blocked = isBlocked });
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
            else if (path == "/report")
            {
                var domain = request.QueryString["domain"];
                if (!string.IsNullOrEmpty(domain) && _currentPolicy?.ActiveSessionId != null)
                {
                    await _attemptTracker.RecordWebBlockedAsync(_currentPolicy.ActiveSessionId.Value, domain);
                }
                response.StatusCode = (int)HttpStatusCode.OK;
            }
        }
        catch
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    // --- App Blocking Logic ---

    private void OnProcessKillTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        => KillBlockedProcessesImmediate();

    private void KillBlockedProcessesImmediate()
    {
        var processes = Process.GetProcesses();
        foreach (var p in processes)
        {
            try
            {
                // TIER 0: Self-Protection
                if (p.Id == _currentPid) continue;

                string pName = p.ProcessName;

                // TIER 1: Immutable Allowlist
                if (SystemSafetyConstants.UnkillableProcesses.Contains(pName)) continue;

                // TIER 2: Process Name Check (Is it even in the user's block list?)
                if (!_blockedProcessNames.Contains(pName)) continue;

                // TIER 3: Path & Signature Protection (The Shield)
                string? filePath = GetProcessPath(p);
                if (string.IsNullOrEmpty(filePath)) continue; // Skip if we can't see the path

                if (IsSystemProtected(filePath))
                {
                    Debug.WriteLine($"[NenFocus] Shielded protected process: {pName}");
                    continue;
                }

                // If we passed all safety tiers, kill the tree
                if (_currentPolicy?.ActiveSessionId != null)
                {
                    _ = _attemptTracker.RecordAppBlockedAsync(_currentPolicy.ActiveSessionId.Value, pName);
                }

                p.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NenFocus] Safety skip for {p.ProcessName}: {ex.Message}");
            }
            finally { p.Dispose(); }
        }
    }

    private bool IsSystemProtected(string filePath)
    {
        // 1. Path check (unchanged)
        if (filePath.Contains(@"C:\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains(@"C:\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Digital Signature Check (Updated for .NET 9)
        return _trustedSignatureCache.GetOrAdd(filePath, path =>
        {
            try
            {
                // Use X509CertificateLoader instead of CreateFromSignedFile
                using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
                string subject = cert.Subject;

                return subject.Contains(SystemSafetyConstants.MicrosoftPublisher) ||
                       subject.Contains(SystemSafetyConstants.NenFocusPublisher);
            }
            catch
            {
                return false;
            }
        });
    }

    private string? GetProcessPath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }

    // --- Hosts & Registry Logic ---

    private async Task ApplyHostsRulesAsync(PolicySliceDto policy, CancellationToken ct)
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

    private static void FlushDns()
    {
        try { Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true })?.WaitForExit(2000); }
        catch { }
    }

    private void EnsureRegistryPolicies()
    {
        string[] browserKeys = {
            @"SOFTWARE\Policies\BraveSoftware\Brave",
            @"SOFTWARE\Policies\Google\Chrome",
            @"SOFTWARE\Policies\Microsoft\Edge"
        };
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

    private static string NormalizeProcessName(string app)
    {
        var trimmed = app.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    private static string NormalizeDomain(string site)
    {
        if (string.IsNullOrWhiteSpace(site)) return string.Empty;
        var s = site.Trim().ToLowerInvariant();
        if (s.Contains("://")) s = s.Split("://")[1];

        // Split by path, query, and port to get clean domain
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

    public void Dispose()
    {
        _serverCts?.Cancel();
        _httpListener?.Stop();
        _processKillTimer.Stop();
        _processKillTimer.Dispose();
        _lock.Dispose();
    }
}