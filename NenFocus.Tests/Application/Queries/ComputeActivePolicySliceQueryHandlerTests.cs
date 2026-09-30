using FluentAssertions;
using TestAt = NenFocus.Tests.At;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputeActivePolicySliceQueryHandlerTests
{
    private static readonly DateTime At = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPolicyEngine _policyEngine = Substitute.For<IPolicyEngine>();

    public ComputeActivePolicySliceQueryHandlerTests()
    {
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
    }

    private ComputeActivePolicySliceQueryHandler CreateHandler() => new(_blocks, _presets, _sessions, _policyEngine);

    [Fact]
    public async Task HandleAsync_FetchesBlocksForOneSecondWindowAroundAt()
    {
        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

        await _blocks.Received(1).GetForRangeAsync(new TimeSlot(TestAt.Utc(At.AddSeconds(-1)), TestAt.Utc(At.AddSeconds(1))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverThePolicyEngineComputes()
    {
        var expectedSlice = new ActivePolicySlice { SliceStart = TestAt.Utc(At), BlockedSites = new HashSet<string> { "youtube.com" } };
        _policyEngine.ComputeSlice(TestAt.Utc(At), Arg.Any<IEnumerable<BlockPreset>>(), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>())
            .Returns(expectedSlice);

        var result = await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

        result.Should().BeSameAs(expectedSlice);
    }

    [Fact]
    public async Task HandleAsync_ResolvesDistinctPresetsReferencedByBlocksAndSessions()
    {
        var sharedPresetId = Guid.NewGuid();
        var sessionOnlyPresetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), sharedPresetId,TestAt.Slot(At, At.AddHours(1)));
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            sessionOnlyPresetId, SessionOrigin.Spontaneous);
        session.Start(TestAt.Utc(At));

        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[session]);

        var sharedPreset = BlockPreset.Create(sharedPresetId, "Shared", WebsiteFilterType.Blocklist, BlockPresetType.Focus, TestAt.Utc(At));
        var sessionPreset = BlockPreset.Create(sessionOnlyPresetId, "SessionOnly", WebsiteFilterType.Blocklist, BlockPresetType.Focus, TestAt.Utc(At));
        _presets.GetByIdAsync(sharedPresetId, Arg.Any<CancellationToken>()).Returns(sharedPreset);
        _presets.GetByIdAsync(sessionOnlyPresetId, Arg.Any<CancellationToken>()).Returns(sessionPreset);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            TestAt.Utc(At),
            Arg.Is<IEnumerable<BlockPreset>>(p => p.Count() == 2 && p.Contains(sharedPreset) && p.Contains(sessionPreset)),
            Arg.Is<IEnumerable<Session>>(s => s.Contains(session)),
            Arg.Is<IEnumerable<PlannedBlock>>(b => b.Contains(block)));
    }

    [Fact]
    public async Task HandleAsync_CancelledBlock_PassedThroughForPolicyEngineToRuleOut()
    {
        // The handler no longer filters cancelled blocks: a cancelled block's own IsActive
        // says no (PlannedBlock.EffectiveEnd), and PolicyEngine asks it — see
        // PolicyEngineTests.ComputeSlice_CancelledBlockHandedIn_BlocksNothing.
        var cancelledBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At, At.AddHours(1)));
        cancelledBlock.MarkCancelled(TestAt.Utc(At));

        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[cancelledBlock]);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            TestAt.Utc(At),
            Arg.Any<IEnumerable<BlockPreset>>(),
            Arg.Any<IEnumerable<Session>>(),
            Arg.Is<IEnumerable<PlannedBlock>>(b => b.Single() == cancelledBlock));
    }

    [Fact]
    public async Task HandleAsync_UnresolvablePreset_IsOmittedNotThrown()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,TestAt.Slot(At, At.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            TestAt.Utc(At), Arg.Is<IEnumerable<BlockPreset>>(p => !p.Any()), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>());
    }
}
