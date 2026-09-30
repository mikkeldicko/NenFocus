using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// ISystemClock is the one source of "now" (MODULE_STANDARDS → Application → Forbidden).
/// Only SystemClock reads the machine clock, because it carries the debug offset — any other
/// read is a second clock that silently ignores a debug jump.
/// </summary>
public class ClockRuleTests
{
    private static readonly Regex MachineClockRead = new(@"\bDateTime(Offset)?\s*\.\s*(UtcNow|Now|Today)\b");

    private const string Home = "NenFocus.Infrastructure/Services/SystemClock.cs";

    private static readonly string[] Exceptions =
    [
        // Permanent: a toast's expiry is Windows's own wall-clock time, where the debug offset would be wrong.
        "NenFocus.Infrastructure/Services/WindowsNotificationService.cs",
    ];

    [Fact]
    public void OnlySystemClockReadsTheMachineClock()
    {
        var violations = SourceCode
            .Files("NenFocus.Domain", "NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => f.Path != Home && MachineClockRead.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, Exceptions);
    }

    private static readonly Regex MachineZoneRead = new(@"\bTimeZoneInfo\s*\.\s*Local\b");

    /// <summary>
    /// The user's time zone comes from ISystemClock.TimeZone (Gap 88). Reading the machine's zone
    /// anywhere else would be a second source — and code a test can't run under a fixed zone.
    /// </summary>
    [Fact]
    public void OnlySystemClockReadsTheMachineTimeZone()
    {
        var violations = SourceCode
            .Files("NenFocus.Domain", "NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => f.Path != Home && MachineZoneRead.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// A fake clock nobody set runs at year 0001 and passes silently — it hid an empty Work-Hours check
    /// for months, and a Planner view test ran in the wrong week (Gap 88 close-out, lessons 1 and 12).
    /// TestClock.Create() makes one that throws until it's set, so every fake goes through it.
    /// </summary>
    [Fact]
    public void TestsMakeFakeClocksOnlyThroughTestClock()
    {
        var violations = SourceCode.Files("NenFocus.Tests")
            .Where(f => f.Path != "NenFocus.Tests/TestClock.cs"
                        && Regex.IsMatch(f.Code, @"Substitute\s*\.\s*For\s*<\s*[\w.]*\bISystemClock\s*>"))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }
}
