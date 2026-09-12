using FluentAssertions;
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
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPolicyEngine _policyEngine = Substitute.For<IPolicyEngine>();

    public ComputeActivePolicySliceQueryHandlerTests()
    {
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
    }

    private ComputeActivePolicySliceQueryHandler CreateHandler() => new(_blocks, _presets, _sessions, _contracts, _policyEngine);

    [Fact]
    public async Task HandleAsync_FetchesBlocksForOneSecondWindowAroundAt()
    {
        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        await _blocks.Received(1).GetForRangeAsync(At.AddSeconds(-1), At.AddSeconds(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverThePolicyEngineComputes()
    {
        var expectedSlice = new ActivePolicySlice { SliceStart = At, ActiveBlockPresetType = BlockPresetType.Focus };
        _policyEngine.ComputeSlice(At, Arg.Any<IEnumerable<BlockPreset>>(), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>(), Arg.Any<IEnumerable<NenContract>>())
            .Returns(expectedSlice);

        var result = await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        result.Should().BeSameAs(expectedSlice);
    }

    [Fact]
    public async Task HandleAsync_ResolvesDistinctPresetsReferencedByBlocksAndSessions()
    {
        var sharedPresetId = Guid.NewGuid();
        var sessionOnlyPresetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), sharedPresetId, At, At.AddHours(1));
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            sessionOnlyPresetId, SessionOrigin.Spontaneous);
        session.Start(At);

        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[session]);

        var sharedPreset = BlockPreset.Create(sharedPresetId, "Shared", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At);
        var sessionPreset = BlockPreset.Create(sessionOnlyPresetId, "SessionOnly", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At);
        _presets.GetByIdAsync(sharedPresetId, Arg.Any<CancellationToken>()).Returns(sharedPreset);
        _presets.GetByIdAsync(sessionOnlyPresetId, Arg.Any<CancellationToken>()).Returns(sessionPreset);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            At,
            Arg.Is<IEnumerable<BlockPreset>>(p => p.Count() == 2 && p.Contains(sharedPreset) && p.Contains(sessionPreset)),
            Arg.Is<IEnumerable<Session>>(s => s.Contains(session)),
            Arg.Is<IEnumerable<PlannedBlock>>(b => b.Contains(block)),
            Arg.Any<IEnumerable<NenContract>>());
    }

    [Fact]
    public async Task HandleAsync_CancelledBlock_ExcludedFromPresetsAndFromBlocksPassedToPolicyEngine()
    {
        var cancelledPresetId = Guid.NewGuid();
        var liveBlock = FocusBlock.Create(Guid.NewGuid(), cancelledPresetId, At, At.AddHours(1));
        liveBlock.MarkCancelled(At);

        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[liveBlock]);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        await _presets.DidNotReceive().GetByIdAsync(cancelledPresetId, Arg.Any<CancellationToken>());
        _policyEngine.Received(1).ComputeSlice(
            At,
            Arg.Any<IEnumerable<BlockPreset>>(),
            Arg.Any<IEnumerable<Session>>(),
            Arg.Is<IEnumerable<PlannedBlock>>(b => !b.Any()),
            Arg.Any<IEnumerable<NenContract>>());
    }

    [Fact]
    public async Task HandleAsync_UnresolvablePreset_IsOmittedNotThrown()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, At, At.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            At, Arg.Is<IEnumerable<BlockPreset>>(p => !p.Any()), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>(),
            Arg.Any<IEnumerable<NenContract>>());
    }
}
