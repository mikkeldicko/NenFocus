namespace NenFocus.Domain.Enums;

/// <summary>Why a picked or typed app can't be blocked (<c>BlockedApp.Check</c>, Gap 112).</summary>
public enum BlockedAppRefusal
{
    /// <summary>Nothing that names a process.</summary>
    Empty,

    /// <summary>A Windows process the system can't run without (explorer, svchost…), or NenFocus itself.</summary>
    CriticalProcess
}
