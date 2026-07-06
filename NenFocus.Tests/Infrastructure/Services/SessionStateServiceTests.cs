using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Services;

public class SessionStateServiceTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult> _sweep =
        Substitute.For<ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>>();

    public SessionStateServiceTests()
    {
        _sweep.HandleAsync(Arg.Any<SweepMissedFocusBlocksCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SweepMissedFocusBlocksResult());
    }

    private SessionStateService CreateService() => new(_sessions, _presets, _sweep);

    private static Session CreateActiveSession(Guid presetId)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow);
        return session;
    }

    [Fact]
    public async Task InitializeAsync_NoActiveSession_LeavesStateNull()
    {
        _sessions.GetActiveAsync().Returns((Session?)null);
        var service = CreateService();
        var raised = false;
        service.SessionChanged += (_, _) => raised = true;

        await service.InitializeAsync();

        service.ActiveSession.Should().BeNull();
        raised.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ActiveSessionExists_MapsPresetNameAndRaisesSessionChanged()
    {
        var presetId = Guid.NewGuid();
        var session = CreateActiveSession(presetId);
        _sessions.GetActiveAsync().Returns(session);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var service = CreateService();
        SessionDto? raisedWith = null;
        service.SessionChanged += (_, dto) => raisedWith = dto;

        await service.InitializeAsync();

        service.ActiveSession!.PresetName.Should().Be("Deep Work");
        raisedWith.Should().BeSameAs(service.ActiveSession);
    }

    [Fact]
    public async Task InitializeAsync_PresetNotFound_UsesUnknownPresetPlaceholder()
    {
        var presetId = Guid.NewGuid();
        var session = CreateActiveSession(presetId);
        _sessions.GetActiveAsync().Returns(session);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);
        var service = CreateService();

        await service.InitializeAsync();

        service.ActiveSession!.PresetName.Should().Be("Unknown Preset");
    }

    [Fact]
    public void SetActive_UpdatesStateAndRaisesBothEvents()
    {
        var service = CreateService();
        SessionDto? sessionChangedWith = null;
        RitualRequirementChange? ritualChangedWith = null;
        service.SessionChanged += (_, dto) => sessionChangedWith = dto;
        service.RitualRequirementChanged += (_, change) => ritualChangedWith = change;
        var dto = new SessionDto { Id = Guid.NewGuid(), Status = SessionStatus.Active };

        service.SetActive(dto);

        service.ActiveSession.Should().Be(dto);
        service.IsRitualRequired.Should().BeFalse();
        service.PendingFocusBlockId.Should().BeNull();
        sessionChangedWith.Should().Be(dto);
        ritualChangedWith!.Required.Should().BeFalse();
        ritualChangedWith.FocusBlockId.Should().BeNull();
    }

    [Fact]
    public void SetRitualRequired_True_SetsPendingBlockIdAndRaisesEvent()
    {
        var service = CreateService();
        var blockId = Guid.NewGuid();
        RitualRequirementChange? raisedWith = null;
        service.RitualRequirementChanged += (_, change) => raisedWith = change;

        service.SetRitualRequired(true, blockId);

        service.IsRitualRequired.Should().BeTrue();
        service.PendingFocusBlockId.Should().Be(blockId);
        raisedWith!.Required.Should().BeTrue();
        raisedWith.FocusBlockId.Should().Be(blockId);
        raisedWith.HandoffFromFocusBlockId.Should().BeNull();
    }

    [Fact]
    public void SetRitualRequired_SameBlockTwiceInARow_RaisesEventOnlyOnce()
    {
        var service = CreateService();
        var blockId = Guid.NewGuid();
        var raiseCount = 0;
        service.SetRitualRequired(true, blockId);
        service.RitualRequirementChanged += (_, _) => raiseCount++;

        service.SetRitualRequired(true, blockId);

        raiseCount.Should().Be(0);
    }

    [Fact]
    public void SetRitualRequired_True_DifferentBlockWhileAlreadyRequired_RaisesEventWithHandoffId()
    {
        var service = CreateService();
        var blockA = Guid.NewGuid();
        var blockB = Guid.NewGuid();
        service.SetRitualRequired(true, blockA);
        RitualRequirementChange? raisedWith = null;
        service.RitualRequirementChanged += (_, change) => raisedWith = change;

        // Back-to-back scheduled blocks: the heartbeat reports B as active in the
        // same tick A's window closes, with no intervening false transition.
        service.SetRitualRequired(true, blockB);

        service.PendingFocusBlockId.Should().Be(blockB);
        raisedWith!.Required.Should().BeTrue();
        raisedWith.FocusBlockId.Should().Be(blockB);
        raisedWith.HandoffFromFocusBlockId.Should().Be(blockA);
    }

    [Fact]
    public void SetRitualRequired_False_ClearsPendingBlockId()
    {
        var service = CreateService();
        service.SetRitualRequired(true, Guid.NewGuid());

        service.SetRitualRequired(false);

        service.PendingFocusBlockId.Should().BeNull();
        service.IsRitualRequired.Should().BeFalse();
    }

    [Fact]
    public void SetRitualRequired_False_EventCarriesThePreviouslyPendingBlockId()
    {
        var service = CreateService();
        var blockId = Guid.NewGuid();
        service.SetRitualRequired(true, blockId);
        RitualRequirementChange? raisedWith = null;
        service.RitualRequirementChanged += (_, change) => raisedWith = change;

        // Mirrors ScheduledEnforcementService's call shape: no block id supplied on release.
        service.SetRitualRequired(false);

        raisedWith!.Required.Should().BeFalse();
        raisedWith.FocusBlockId.Should().Be(blockId);
    }

    [Fact]
    public void Clear_ResetsEverythingAndRaisesBothEvents()
    {
        var service = CreateService();
        service.SetActive(new SessionDto { Id = Guid.NewGuid() });
        SessionDto? sessionChangedWith = new(); // sentinel, will be overwritten to null
        RitualRequirementChange? ritualChangedWith = null;
        service.SessionChanged += (_, dto) => sessionChangedWith = dto;
        service.RitualRequirementChanged += (_, change) => ritualChangedWith = change;

        service.Clear();

        service.ActiveSession.Should().BeNull();
        service.IsRitualRequired.Should().BeFalse();
        service.PendingFocusBlockId.Should().BeNull();
        sessionChangedWith.Should().BeNull();
        ritualChangedWith!.Required.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_SweepFindsMissedSessions_RaisesMissedSessionsDiscovered()
    {
        _sessions.GetActiveAsync().Returns((Session?)null);
        var missed = new PostSessionDto { SessionId = Guid.NewGuid(), Status = SessionStatus.Missed };
        _sweep.HandleAsync(Arg.Any<SweepMissedFocusBlocksCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SweepMissedFocusBlocksResult { MissedSessions = [missed] });
        var service = CreateService();
        IReadOnlyList<PostSessionDto>? raisedWith = null;
        service.MissedSessionsDiscovered += (_, sessions) => raisedWith = sessions;

        await service.InitializeAsync();

        raisedWith.Should().ContainSingle().Which.Should().Be(missed);
    }

    [Fact]
    public async Task InitializeAsync_SweepFindsNothing_DoesNotRaiseMissedSessionsDiscovered()
    {
        _sessions.GetActiveAsync().Returns((Session?)null);
        var service = CreateService();
        var raised = false;
        service.MissedSessionsDiscovered += (_, _) => raised = true;

        await service.InitializeAsync();

        raised.Should().BeFalse();
    }
}
