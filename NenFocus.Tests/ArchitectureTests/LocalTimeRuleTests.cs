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
        // Gap 99: EF's storage converter silently converts a non-UTC value on save — a hidden second
        // copy of the rule. Once stored times are UTC by type, it throws instead of converting.
        "NenFocus.Infrastructure/Persistence/NenFocusDbContext.cs",

        // Gap 88 phase 3: their session-clash messages move into the placement verdict.
        "NenFocus.Application/UseCases/Planner/CreatePlannedBlock/CreatePlannedBlockCommandHandler.cs",
        "NenFocus.Application/UseCases/Planner/UpdatePlannedBlock/UpdatePlannedBlockCommandHandler.cs",

        // Gap 97: "which UTC window is this local day" re-derived in the repository (the UTC-days trap).
        "NenFocus.Infrastructure/Persistence/Repositories/PlannedBlockRepository.cs",

        // Gap 89 area 3 (three clock abstractions, Gap 92 A7): LocalNow bypasses _clock.ToLocal.
        "NenFocus.Infrastructure/Services/PlannerClockService.cs",

        // Gap 89: ViewModels converting times themselves.
        "NenFocus/ViewModels/Components/PlannerDragMath.cs",
        "NenFocus/ViewModels/DevTools/DebugClockViewModel.cs",
        "NenFocus/ViewModels/Pages/PlannerPageViewModel.cs",
        "NenFocus/ViewModels/Pages/PostSessionViewModel.cs",
        "NenFocus/ViewModels/Ritual/HatsuPageViewModel.cs",
        "NenFocus/ViewModels/Ritual/RitualShellViewModel.cs",
        "NenFocus/Views/PlannerPageView.axaml.cs",
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
