using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only BlockPlacementChecker decides placement (Gap 88): "may this block go here" — past, contract
/// hours, free for Focus — is answered once, in the Domain. Outside it, nobody asks whether two times
/// overlap: that question is how the deleted copies started (WouldCollide, FindSplitOverlap, the
/// handlers' own overlap checks). A text scan; the Domain's own SQL filters (TimeSlot.Filters) are
/// the sanctioned way for a repository to narrow and are left out.
/// </summary>
public class PlacementRuleTests
{
    private static readonly Regex OverlapCall = new(@"(?<!Filters)\.(Overlaps|OverlapsWith)\s*\(");

    private static readonly string[] OverlapExceptions =
    [
        // Split's two halves against each other — a rule about the split itself, not placement
        // (the halves aren't blocks yet). The validator decides; the dialog's live warning asks the same.
        "NenFocus.Application/UseCases/Planner/SplitPlannedBlock/SplitPlannedBlockValidator.cs has 1",
        "NenFocus/ViewModels/Planner/PlannerPageViewModel.cs has 1",
    ];

    [Fact]
    public void OnlyTheDomainAsksWhetherTimesOverlap()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Select(f => (f.Path, Count: OverlapCall.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, OverlapExceptions);
    }

    /// <summary>"Focus-only" was a flag each caller had to pass to the repository (Gap 88 table row #1);
    /// the checker knows it now. The flag must not come back anywhere, the Domain included.</summary>
    [Fact]
    public void NoCallerChoosesRitualOnly()
    {
        var violations = SourceCode.Files("NenFocus.Domain", "NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => Regex.IsMatch(f.Code, @"\britualOnly\b"))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }
}
