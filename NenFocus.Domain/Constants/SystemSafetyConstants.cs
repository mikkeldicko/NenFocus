namespace NenFocus.Domain.Constants;

public static class SystemSafetyConstants
{
    public static readonly HashSet<string> UnkillableProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "taskmgr", "lsass", "csrss", "wininit", "services",
        "winlogon", "smss", "svchost", "fontdrvhost", "dwm", "nenfocus"
    };
}
