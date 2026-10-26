using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetActiveSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetActiveSessionQueryHandlerTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private GetActiveSessionQueryHandler CreateHandler() => new(_sessions, _blocks, _presets);

    [Fact]
    public async Task HandleAsync_NoActiveSession_ReturnsNull()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ActiveSession_MapsPresetNameAndFields()
    {
        var presetId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), presetId, At.Slot(now, now.AddMinutes(45)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Deep work"), block, At.Utc(now));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().NotBeNull();
        result!.PresetName.Should().Be("Deep Work");
        result.Goal.Should().Be("Deep work");
        result.Status.Should().Be(SessionStatus.Active);
        result.Timeline!.Breaks.IsEmpty.Should().BeTrue();
    }

    // Gap 89 F1/F2: the restart path used to lose its originating block, so after a restart
    // the Planner no longer knew which tile was running. The one builder carries it (Gap 92, A3).
    [Fact]
    public async Task HandleAsync_PlannedSession_CarriesOriginatingBlockAndPreset()
    {
        var presetId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var block = FocusBlock.Create(blockId, presetId, At.Slot(now, now.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Goal"), block, At.Utc(now));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.OriginatingBlockId.Should().Be(blockId);
        result.PresetName.Should().Be("Deep Work");
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_ReturnsUnknownPresetName()
    {
        var now = DateTime.UtcNow;
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(now, now.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Goal"), block, At.Utc(now));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(block.BlockPresetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.PresetName.Should().Be("Unknown");
    }

    [Fact]
    public async Task HandleAsync_WithBreakSchedule_MapsBreaksAndPresetLabel()
    {
        var now = DateTime.UtcNow;
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(now, now.AddMinutes(60)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Goal"), block, At.Utc(now),
            breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(60)),
            breakPresetName: "Classic");
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(block.BlockPresetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.BreakPresetName.Should().Be("Classic");
        result.Timeline!.Breaks.Breaks.Should().ContainSingle().Which.Should().Be(
            new SessionBreak(BreakKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)));
    }
}
