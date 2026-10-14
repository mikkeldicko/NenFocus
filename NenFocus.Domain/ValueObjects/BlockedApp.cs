using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// An app, as its lowercased process name (<c>steam</c>) — the one definition the preset, a session's
/// extra apps, the slice and the kill sweep share (Gap 112, I5). A picked path
/// (<c>C:\Games\Steam.exe</c>) is cut down to that name on construction, and a critical system process
/// can never be one. Desktop-only: iOS blocks picker tokens, not names (Gap 77).
/// </summary>
public record BlockedApp
{
    // Windows can't run without these; NenFocus must never kill itself.
    private static readonly HashSet<string> CriticalProcesses =
    [
        "explorer", "taskmgr", "lsass", "csrss", "wininit", "services",
        "winlogon", "smss", "svchost", "fontdrvhost", "dwm", "nenfocus"
    ];

    public string ProcessName { get; }

    public BlockedApp(string processName)
    {
        if (Check(processName) is { } refusal)
            throw new ArgumentException($"'{processName}' can't be blocked: {refusal}.", nameof(processName));

        ProcessName = NameOf(processName);
    }

    /// <summary>The non-throwing ask, for what a person picked or typed: why it can't be blocked, or null.</summary>
    public static BlockedAppRefusal? Check(string? input)
    {
        var name = NameOf(input);
        if (name.Length == 0) return BlockedAppRefusal.Empty;
        return CriticalProcesses.Contains(name) ? BlockedAppRefusal.CriticalProcess : null;
    }

    /// <summary>The process name a path or name stands for — what the picker shows before anything is refused.</summary>
    public static string NameOf(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName)) return string.Empty;

        var name = Path.GetFileName(pathOrName.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Trim().ToLowerInvariant();
    }

    /// <summary>Whether a running process (Windows reports it as <c>Steam</c>, no extension) is this app.</summary>
    public bool Matches(string runningProcessName) => NameOf(runningProcessName) == ProcessName;

    public override string ToString() => ProcessName;
}
