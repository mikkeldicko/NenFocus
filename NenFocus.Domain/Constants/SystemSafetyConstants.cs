namespace NenFocus.Domain.Constants;

public static class SystemSafetyConstants
{
    public static readonly HashSet<string> UnkillableProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "taskmgr", "lsass", "csrss", "wininit", "services",
        "winlogon", "smss", "svchost", "fontdrvhost", "dwm", "nenfocus"
    };

    public const string MicrosoftPublisher = "O=Microsoft Corporation";
    public const string NenFocusPublisher = "PC-NenFocus-Developer"; // Update to your actual cert subject
}