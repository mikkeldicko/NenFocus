using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Converting between UTC and the user's local time is one rule with one home: ISystemClock
/// (ToLocal/ToUtc), implemented by SystemClock. Every other ToLocalTime()/ToUniversalTime()
/// is a second copy of that rule — and the likeliest place for a DST or date-kind bug.
/// </summary>
public class LocalTimeRuleTests
{
    private static readonly Regex LocalTimeConversion = new(@"\.\s*(ToLocalTime|ToUniversalTime)\s*\(");

    private const string Home = "NenFocus.Infrastructure/Services/SystemClock.cs";

    private static readonly string[] Exceptions =
    [
        // Gap 97: "which UTC window is this local day" re-derived in the repository (the UTC-days trap).
        "NenFocus.Infrastructure/Persistence/Repositories/PlannedBlockRepository.cs",

        // Gap 89: the debug clock converts a user-typed time to UTC for SetTo.
        "NenFocus/ViewModels/DevTools/DebugClockViewModel.cs",
    ];

    [Fact]
    public void OnlySystemClockConvertsBetweenUtcAndLocal()
    {
        var violations = SourceCode
            .Files("NenFocus.Domain", "NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => f.Path != Home && LocalTimeConversion.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, Exceptions);
    }
}
