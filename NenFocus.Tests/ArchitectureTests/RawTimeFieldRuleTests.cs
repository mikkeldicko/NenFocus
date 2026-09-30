using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only the Domain combines raw time fields. Comparing or subtracting Start/End/StartedAt/… outside
/// the Domain is a business question answered a second time ("has it started", "do they overlap",
/// "how long") — MODULE_STANDARDS → Application Handler Forbidden Zone, and the ViewModel's No Math.
///
/// A bridge (Guard Hierarchy rank 2) until Gap 90 lets the compiler take over (blocks and sessions
/// hold a TimeSlot, no raw fields left to compare) — delete this test then. A text scan: it can't tell
/// an entity's Start from a DTO's, so each entry carries a reason. Entries are "File has N", so a new
/// comparison in an already-listed file is caught too. Repositories and the DbContext are left out:
/// SQL must mirror entity rules, and parity tests guard those copies.
/// </summary>
public class RawTimeFieldRuleTests
{
    private const string Field = @"\.(Start|End|StartedAt|EndedAt|RitualCompletedAt|SessionEndedAt|CancelledAt|StartsAt|EndsAt|EndAt)\b";

    // A comparison or subtraction on either side of the field — but not the "=>" of a lambda.
    private static readonly Regex RawUse = new(
        Field + @"\s*(<=|>=|(?<!=)>|<|==|!=|-(?![-=])|\+(?![+=]))"
        + @"|(<=|>=|(?<!=)>|<|==|!=|(?<![-=])-|(?<![+=])\+)\s*[A-Za-z_][A-Za-z0-9_.!?]*" + Field);

    // The same comparison written as a method call: FluentValidation's comparison rules pointed at a
    // time field (.GreaterThan(x => x.Start)), CompareTo / DateTime.Compare, and Math.Min/Max.
    private static readonly Regex RawUseAsCall = new(
        @"\.(GreaterThan|GreaterThanOrEqualTo|LessThan|LessThanOrEqualTo|Equal|NotEqual|InclusiveBetween|ExclusiveBetween)\s*\(\s*\w+\s*=>\s*[\w.!?]*" + Field
        + "|" + Field + @"\s*\.\s*CompareTo\s*\("
        + @"|\bDateTime\s*\.\s*Compare\s*\([^;]*" + Field
        + @"|\bMath\s*\.\s*(Min|Max)\s*\([^;]*" + Field);

    private static readonly string[] KnownRawUses =
    [
        // Gap 88 — one source for time slots and placement:
        // display end re-derived (SessionEndedAt < End) though FocusBlock.EffectiveEnd answers it.
        "NenFocus.Application/Queries/GetPlannedBlocksForDay/GetPlannedBlocksForDayQueryHandler.cs has 2",
        // "do these overlap" — Cover Deficit moves onto the placement checker.
        "NenFocus.Application/Queries/GetSuggestedRedemptionPlacements/GetSuggestedRedemptionPlacementsQueryHandler.cs has 2",
        // "do these overlap" in the UI — UI checks built on the checker.
        "NenFocus/ViewModels/Components/PlannerDragMath.cs has 2",
        "NenFocus/ViewModels/Pages/PlannerPageViewModel.cs has 2",
        // end after start, ≥ 1 minute, Focus ≤ 12 h — copies of FocusBlock.Create's own rules.
        "NenFocus.Application/UseCases/Planner/CreatePlannedBlock/CreatePlannedBlockValidator.cs has 3",
        "NenFocus.Application/UseCases/Planner/UpdatePlannedBlock/UpdatePlannedBlockValidator.cs has 2",
        // "end after start" on input — a rule TimeSlot's constructor will own (Gaps 88/99).
        "NenFocus.Application/UseCases/NenContracts/Seal/SealNenContractValidator.cs has 1",
        "NenFocus.Application/UseCases/NenContracts/Edit/EditNenContractValidator.cs has 1",

        // Gap 89 — "has the block ended" decided in the UI.
        "NenFocus/ViewModels/Ritual/HatsuPageViewModel.cs has 1",
        "NenFocus/ViewModels/ShellViewModel.cs has 1",

        // Gap 90 — lengths and "how long until" move onto TimeSlot:
        // session length (Session.RunLength already exists) and break progress.
        "NenFocus.Infrastructure/Services/ActiveSessionMonitor.cs has 3",
        // block length.
        "NenFocus.Application/UseCases/Sessions/StartSession/StartSessionCommandHandler.cs has 1",
        // "how late".
        "NenFocus.Application/Queries/GetFocusBlockRitualContext/GetFocusBlockRitualContextQueryHandler.cs has 1",
        // minutes until start, for the wording.
        "NenFocus.Application/Services/BlockMarkerNotifier.cs has 1",

        // Allowed: drag/resize pixel geometry on tile positions (MODULE_STANDARDS → UI → Views).
        // Stays until this test is deleted at Gap 90; its business rules are caught by the local-time rule.
        "NenFocus/Views/PlannerPageView.axaml.cs has 6",
    ];

    [Fact]
    public void OnlyTheDomainCombinesRawTimeFields()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => !f.Path.StartsWith("NenFocus.Infrastructure/Persistence/", StringComparison.Ordinal))
            .Select(f => (f.Path, Count: RawUse.Matches(f.Code).Count + RawUseAsCall.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownRawUses);
    }
}
