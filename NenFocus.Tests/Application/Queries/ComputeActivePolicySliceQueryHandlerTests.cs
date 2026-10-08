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

/// <summary>
/// The handler fetches; the real PolicyEngine decides (Gap 111 — a Domain service is never faked).
/// The merge rules themselves are PolicyEngineTests' job; these check that what the handler
/// fetches reaches the engine.
/// </summary>
public class ComputeActivePolicySliceQueryHandlerTests
{
    private static readonly DateTime At = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public ComputeActivePolicySliceQueryHandlerTests()
    {
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private ComputeActivePolicySliceQueryHandler CreateHandler() => new(_blocks, _presets, _sessions, new PolicyEngine());

    private BlockPreset PresetBlocking(string site)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), site, WebsiteFilterType.Blocklist, BlockPresetType.Focus, TestAt.Utc(At));
        preset.AddWebsite(new BlockedSite(site));
        _presets.GetByIdAsync(preset.Id, Arg.Any<CancellationToken>()).Returns(preset);
        return preset;
    }

    private Task<ActivePolicySlice> Compute() =>
        CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(TestAt.Utc(At)), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_FetchesBlocksForOneSecondWindowAroundAt()
    {
        await Compute();

        await _blocks.Received(1).GetForRangeAsync(new TimeSlot(TestAt.Utc(At.AddSeconds(-1)), TestAt.Utc(At.AddSeconds(1))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NothingActive_ReturnsAnEmptySlice()
    {
        var result = await Compute();

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ResolvesPresetsReferencedByBlocksAndSessions_OnceEach()
    {
        var blockPreset = PresetBlocking("youtube.com");
        var sessionPreset = PresetBlocking("reddit.com");
        var block = FocusBlock.Create(Guid.NewGuid(), blockPreset.Id, TestAt.Slot(At, At.AddHours(1)));
        var otherBlock = BackgroundBlock.Create(Guid.NewGuid(), blockPreset.Id, TestAt.Slot(At, At.AddHours(1)));
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            sessionPreset.Id, SessionOrigin.Spontaneous);
        session.Start(TestAt.Utc(At));

        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block, otherBlock]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await Compute();

        result.BlockedSites.Should().BeEquivalentTo(["youtube.com", "reddit.com"]);
        result.ActiveSessionId.Should().Be(session.Id);
        await _presets.Received(1).GetByIdAsync(blockPreset.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CancelledBlock_BlocksNothing()
    {
        // The handler doesn't filter cancelled blocks: a cancelled block's own IsActive says no
        // (PlannedBlock.EffectiveEnd), and PolicyEngine asks it.
        var preset = PresetBlocking("youtube.com");
        var cancelledBlock = FocusBlock.Create(Guid.NewGuid(), preset.Id, TestAt.Slot(At, At.AddHours(1)));
        cancelledBlock.MarkCancelled(TestAt.Utc(At));

        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[cancelledBlock]);

        var result = await Compute();

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_UnresolvablePreset_IsOmittedNotThrown()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, TestAt.Slot(At, At.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await Compute();

        result.IsEmpty.Should().BeTrue();
    }
}
