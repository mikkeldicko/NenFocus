using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class MarkFocusBlockMissedCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private MarkFocusBlockMissedCommandHandler CreateHandler() => new(_blocks, _sessions, _presets);

    [Fact]
    public async Task HandleAsync_BlockNotFound_Fails()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(new MarkFocusBlockMissedCommand { FocusBlockId = blockId });

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CreatesMissedSessionFromBlockSchedule()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start)));

        var result = await CreateHandler().HandleAsync(new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBeNull();

        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s =>
                s.Origin == SessionOrigin.Planned &&
                s.OriginatingBlockId == block.Id &&
                s.BlockPresetId == presetId &&
                s.Status == SessionStatus.Missed &&
                s.StartedAt == At.Utc(Start) &&
                s.EndedAt == At.Utc(End) &&
                s.Duration.Value == TimeSpan.FromHours(1) &&
                s.Goal.Value == "(missed — no goal set)"),
            Arg.Any<CancellationToken>());

        result.PostSession.Should().NotBeNull();
        result.PostSession!.Status.Should().Be(SessionStatus.Missed);
        result.PostSession.PresetName.Should().Be("Deep Work");
        result.PostSession.StartedAt.Should().Be(At.Utc(Start));
        result.PostSession.EndedAt.Should().Be(At.Utc(End));
        result.PostSession.PlannedDurationMinutes.Should().Be(60);
        result.PostSession.ActualDurationMinutes.Should().Be(0);
        result.PostSession.BreaksTaken.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_PostSessionHasBlankPresetName()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.PostSession!.PresetName.Should().Be(string.Empty);
    }
}
