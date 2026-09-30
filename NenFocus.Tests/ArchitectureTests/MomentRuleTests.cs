using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// A Moment has one public way in and out as a DateTime — FromStorage/ToStorage — and only the
/// places that genuinely meet a raw DateTime may use it (Gap 88, folded-in Gap 99 Q1/Q3). Everyone
/// else keeps moments as moments and asks UserTimeZone for calendar times, so `moment.ToStorage().Date`
/// can't bring back the UTC-date bug of Gaps 65/87/96. The tests' At helper is the third door; it isn't
/// scanned. Bridges added while callers convert (Gap 88 phases 3–5) go on the list tagged with the phase
/// that removes them; phase 6 empties it back to the permanent two.
/// </summary>
public class MomentRuleTests
{
    private static readonly Regex StorageExit = new(@"\b(FromStorage|ToStorage)\s*\(");

    private const string Home = "NenFocus.Domain/ValueObjects/Moment.cs";

    private static readonly string[] Doors =
    [
        // Permanent: the database stores a moment as a UTC DateTime column.
        "NenFocus.Infrastructure/Persistence/NenFocusDbContext.cs",
        // Permanent: the machine clock (DateTime.UtcNow + debug offset) is where "now" enters.
        "NenFocus.Infrastructure/Services/SystemClock.cs",
    ];

    [Fact]
    public void OnlyStorageAndTheClockTurnAMomentIntoADateTime()
    {
        var violations = SourceCode
            .Files("NenFocus.Domain", "NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => f.Path != Home && StorageExit.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, Doors);
    }
}
