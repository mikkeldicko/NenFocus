using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

public class TestDoubleRuleTests
{
    // Short or qualified: Substitute.For<ISessionRepository>() and Substitute.For<NenFocus.Application.Interfaces.ISessionRepository>().
    private static readonly Regex RepositoryFake = new(@"Substitute\.For<\s*([A-Za-z_.]+\.)?I[A-Za-z]+Repository\s*>");

    /// <summary>
    /// Tests fake only what lies outside the app (Gap 110): the database is the app's own, so a use
    /// case runs on real SQLite through the real pipeline (<c>UseCaseTestBase</c>) — a faked
    /// repository can't see storage, and hid both the tags that were never saved and Break's
    /// read-after-write crash. Shrink-only: Gap 110 converts the command handler tests, Gap 115 the rest.
    /// </summary>
    [Fact]
    public void ApplicationTestsDoNotFakeRepositories()
    {
        var violations = SourceCode.Files("NenFocus.Tests")
            .Where(f => RepositoryFake.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, StillFakingRepositories);
    }

    private static readonly string[] StillFakingRepositories =
    [
        "NenFocus.Tests/Application/EventHandlers/NenContractActivatedEventHandlerTests.cs",
        "NenFocus.Tests/Application/EventHandlers/NenContractCompletedEventHandlerTests.cs",
        "NenFocus.Tests/Application/EventHandlers/SessionEndedEventHandlerTests.cs",
        "NenFocus.Tests/Application/EventHandlers/SessionStartedEventHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/CheckNenContractCancellableQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/CheckNenContractDateRangeOverlapQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/ComputeActivePolicySliceQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetActiveSessionQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetAllBlockPresetsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetAllContractPresetsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetBlockPlacementVerdictQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetFocusBlockAwaitingRitualQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetFocusBlockRitualContextQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetInEffectNenContractsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetNenContractCompletionReadinessQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetNenContractDeficitsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetNenContractForEditQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetNenContractRecapQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetNenContractsForDateRangeQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetPlannedBlocksForDayQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetSessionSnapshotForBlockQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetSuggestedRedemptionPlacementsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetUnacknowledgedPostContractsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Queries/GetUnacknowledgedPostSessionsQueryHandlerTests.cs",
        "NenFocus.Tests/Application/Services/BlockMarkerNotifierTests.cs",
        "NenFocus.Tests/Application/UseCases/Sessions/MarkFocusBlockMissedValidatorTests.cs",
        "NenFocus.Tests/Application/UseCases/Sessions/StartSessionValidatorTests.cs",
        "NenFocus.Tests/Application/UseCases/Sessions/SweepMissedFocusBlocksCommandHandlerTests.cs",
    ];
}
