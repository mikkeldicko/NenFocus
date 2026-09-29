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
}
