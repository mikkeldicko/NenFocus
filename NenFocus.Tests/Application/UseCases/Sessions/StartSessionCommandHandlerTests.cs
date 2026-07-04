using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class StartSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public StartSessionCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _plannedBlocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private StartSessionCommandHandler CreateHandler()
        => new(_sessions, _plannedBlocks, _sessionState, _profiles, _clock);

    private static StartSessionCommand SpontaneousCommand(Guid? profileId = null) => new()
    {
        Goal = "Deep work",
        ProfileId = profileId ?? Guid.NewGuid(),
        DurationMinutes = 30
    };

    [Fact]
    public async Task HandleAsync_Spontaneous_SavesSessionAndSetsActiveState()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(profileId), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBeNull();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Origin == SessionOrigin.Spontaneous && s.Status == SessionStatus.Active),
            Arg.Any<CancellationToken>());
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d =>
            d.ProfileName == "Deep Work" && d.Origin == SessionOrigin.Spontaneous));
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_StillSucceedsWithBlankProfileName()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(SpontaneousCommand(profileId), CancellationToken.None);

        result.Success.Should().BeTrue();
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d => d.ProfileName == string.Empty));
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
    public async Task HandleAsync_WithBreakSettings_ConvertsToDomainBreakSettings()
    {
        var command = SpontaneousCommand() with
        {
            BreakSettings = new BreakSettingsDto { IntervalMinutes = 25, BreakDurationMinutes = 5 }
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _sessions.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.BreakSettings != null
                && s.BreakSettings.IntervalMinutes == 25
                && s.BreakSettings.BreakLengthMinutes == 5),
            Arg.Any<CancellationToken>());
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
    public async Task HandleAsync_Planned_OriginatingBlockNoLongerExists_StillSucceeds()
    {
        var blockId = Guid.NewGuid();
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var command = SpontaneousCommand() with { OriginatingBlockId = blockId };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _plannedBlocks.DidNotReceive().SaveAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }
}
