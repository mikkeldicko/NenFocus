using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class StartSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IBreakTimerPresetRepository _breakTimerPresets = Substitute.For<IBreakTimerPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public StartSessionCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private StartSessionCommandHandler CreateHandler()
        => new(_sessions, _plannedBlocks, _sessionState, _presets, _breakTimerPresets, _clock);

    private static StartSessionCommand SpontaneousCommand(Guid? presetId = null) => new()
    {
        Goal = "Deep work",
        PresetId = presetId ?? Guid.NewGuid(),
        DurationMinutes = 30
    };

    [Fact]
    public async Task HandleAsync_Spontaneous_SavesSessionAndSetsActiveState()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(presetId), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBeNull();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Origin == SessionOrigin.Spontaneous && s.Status == SessionStatus.Active),
            Arg.Any<CancellationToken>());
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d =>
            d.PresetName == "Deep Work" && d.Origin == SessionOrigin.Spontaneous));
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_StillSucceedsWithBlankPresetName()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(presetId), CancellationToken.None);

        result.Success.Should().BeTrue();
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d => d.PresetName == string.Empty));
    }

    [Fact]
    public async Task HandleAsync_UpcomingFocusBlockWithinDuration_Fails()
    {
        var conflictingBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10), Now.AddHours(1));
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[conflictingBlock]);

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("10 minutes");
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockAlreadyActive_IsRejected()
    {
        var activeBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-5), Now.AddHours(1));
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[activeBlock]);

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Spontaneous_FreedTailAfterEarlyEnd_Succeeds()
    {
        // A Focus block that already ended its session early no longer occupies the rest
        // of its slated window — starting a spontaneous session in that freed tail must
        // not be rejected as "a focus block is currently active."
        var endedEarlyBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-2), Now.AddHours(1));
        endedEarlyBlock.MarkRitualCompleted(Now.AddHours(-2));
        endedEarlyBlock.MarkSessionEnded(Now.AddMinutes(-5));
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[endedEarlyBlock]);

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(), CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithBreakTimerPresetId_SnapshotsBreakTimerOntoSession()
    {
        var breakTimerPresetId = Guid.NewGuid();
        _breakTimerPresets.GetByIdAsync(breakTimerPresetId, Arg.Any<CancellationToken>())
            .Returns(BreakTimerPreset.Create(breakTimerPresetId, "Classic", new BreakTimer(25, 5, 4, 15)));
        var command = SpontaneousCommand() with { BreakTimerPresetId = breakTimerPresetId };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.BreakTimer != null
                && s.BreakTimer.WorkMinutes == 25
                && s.BreakTimer.ShortBreakMinutes == 5
                && s.BreakTimer.CyclesBeforeLongBreak == 4
                && s.BreakTimer.LongBreakMinutes == 15),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BreakTimerPresetNotFound_Fails()
    {
        var breakTimerPresetId = Guid.NewGuid();
        _breakTimerPresets.GetByIdAsync(breakTimerPresetId, Arg.Any<CancellationToken>())
            .Returns((BreakTimerPreset?)null);
        var command = SpontaneousCommand() with { BreakTimerPresetId = breakTimerPresetId };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Planned_MarksOriginatingFocusBlockRitualCompleted()
    {
        var blockId = Guid.NewGuid();
        var focusBlock = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddMinutes(-1), Now.AddHours(1));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(focusBlock);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        focusBlock.RitualCompletedAt.Should().Be(Now);
        await _plannedBlocks.Received(1).SaveAsync(focusBlock, Arg.Any<CancellationToken>());
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Origin == SessionOrigin.Planned && s.OriginatingBlockId == blockId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Planned_SessionEndAtMatchesOriginatingBlockEnd_NotDurationDerived()
    {
        // Regression pin: a Planned session must end exactly when its originating
        // block ends, regardless of how much wizard time elapsed before Start was
        // clicked (which is what DurationMinutes reflects).
        var blockId = Guid.NewGuid();
        var blockEnd = Now.AddMinutes(3);
        var focusBlock = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddMinutes(-2), blockEnd);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(focusBlock);

        // Simulates the Hatsu-computed remaining minutes (rounded, e.g. via the
        // 1-minute floor) diverging from the block's actual precise end time.
        var command = SpontaneousCommand() with { OriginatingBlockId = blockId, DurationMinutes = 5 };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.EndAt == blockEnd),
            Arg.Any<CancellationToken>());
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d => d.EndAt == blockEnd));
    }

    [Fact]
    public async Task HandleAsync_Planned_DurationIsFullBlockLength_NotFromCommandDurationMinutesOrRemainingTime()
    {
        // "Planned" must always be the block's fixed, full nominal length — a schedule
        // fact independent of when the Ritual actually completes — never "however much
        // is left" and never the caller's DurationMinutes (an earlier, Hatsu click-time
        // clock read). This is the invariant that keeps EndSessionCommandHandler's
        // credited "Duration" always ≤ "Planned": a block can't be credited for longer
        // than it was ever scheduled to run.
        var blockId = Guid.NewGuid();
        var blockStart = Now.AddMinutes(-2); // Ritual took 2 minutes
        var blockEnd = Now.AddMinutes(58);
        var focusBlock = FocusBlock.Create(blockId, Guid.NewGuid(), blockStart, blockEnd);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(focusBlock);

        // Deliberately stale/wrong DurationMinutes, as if Hatsu computed it earlier.
        var command = SpontaneousCommand() with { OriginatingBlockId = blockId, DurationMinutes = 999 };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Duration.Value == TimeSpan.FromMinutes(60)), // full block: blockEnd - blockStart
            Arg.Any<CancellationToken>());
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d => d.DurationMinutes == 60));
    }

    [Fact]
    public async Task HandleAsync_Planned_ShortBlock_RitualTakingHalfTheBlock_StillPlansFullBlockLength()
    {
        // The exact scenario that exposed the old (wrong) fix: a 2-minute block where
        // the Ritual itself takes a full minute. "Planned" must show the block's full
        // 2 minutes, not "1 minute remaining" — otherwise a within-grace credited
        // Duration of 2 minutes would exceed a 1-minute Planned figure, which can't
        // make sense for a block that was never scheduled to run more than 2 minutes.
        var blockId = Guid.NewGuid();
        var blockStart = Now.AddMinutes(-1); // Ritual took 1 minute
        var blockEnd = Now.AddMinutes(1);
        var focusBlock = FocusBlock.Create(blockId, Guid.NewGuid(), blockStart, blockEnd);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(focusBlock);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Duration.Value == TimeSpan.FromMinutes(2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Planned_OriginatingBlockNoLongerExists_Fails()
    {
        var blockId = Guid.NewGuid();
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _plannedBlocks.DidNotReceive().SaveAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Planned_DoesNotTreatOwnOriginatingBlockAsConflict()
    {
        var blockId = Guid.NewGuid();
        var ownBlock = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddMinutes(-5), Now.AddMinutes(55));
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[ownBlock]);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(ownBlock);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        ownBlock.RitualCompletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_Planned_OriginatingBlockWindowAlreadyClosed_Fails()
    {
        var blockId = Guid.NewGuid();
        var closedBlock = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddHours(-2), Now.AddHours(-1));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(closedBlock);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Planned_OriginatingBlockEndsExactlyNow_StillAllowed()
    {
        // PlannedBlock.HasEnded(asOf) is the codebase's one canonical "is this past"
        // definition (End < asOf, strict) — this check must match it exactly rather than
        // its own now >= End (inclusive of the exact-equality instant).
        var blockId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddHours(-1), Now);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Planned_SessionAlreadyExistsForBlock_Fails()
    {
        var blockId = Guid.NewGuid();
        var focusBlock = FocusBlock.Create(blockId, Guid.NewGuid(), Now.AddMinutes(-1), Now.AddHours(1));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(focusBlock);
        _sessions.ExistsForOriginatingBlockAsync(blockId, Arg.Any<CancellationToken>()).Returns(true);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
