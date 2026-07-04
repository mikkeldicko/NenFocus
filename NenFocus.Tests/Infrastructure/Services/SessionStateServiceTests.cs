using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Services;

public class SessionStateServiceTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private SessionStateService CreateService() => new(_sessions, _profiles);

    private static Session CreateActiveSession(Guid profileId)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            profileId, SessionOrigin.Spontaneous);
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
    public async Task InitializeAsync_ActiveSessionExists_MapsProfileNameAndRaisesSessionChanged()
    {
        var profileId = Guid.NewGuid();
        var session = CreateActiveSession(profileId);
        _sessions.GetActiveAsync().Returns(session);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        var service = CreateService();
        SessionDto? raisedWith = null;
        service.SessionChanged += (_, dto) => raisedWith = dto;

        await service.InitializeAsync();

        service.ActiveSession!.ProfileName.Should().Be("Deep Work");
        raisedWith.Should().BeSameAs(service.ActiveSession);
    }

    [Fact]
    public async Task InitializeAsync_ProfileNotFound_UsesUnknownProfilePlaceholder()
    {
        var profileId = Guid.NewGuid();
        var session = CreateActiveSession(profileId);
        _sessions.GetActiveAsync().Returns(session);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);
        var service = CreateService();

        await service.InitializeAsync();

        service.ActiveSession!.ProfileName.Should().Be("Unknown Profile");
    }

    [Fact]
    public void SetActive_UpdatesStateAndRaisesBothEvents()
    {
        var service = CreateService();
        SessionDto? sessionChangedWith = null;
        bool? ritualChangedWith = null;
        service.SessionChanged += (_, dto) => sessionChangedWith = dto;
        service.RitualRequirementChanged += (_, required) => ritualChangedWith = required;
        var dto = new SessionDto { Id = Guid.NewGuid(), Status = SessionStatus.Active };

        service.SetActive(dto);

        service.ActiveSession.Should().Be(dto);
        service.IsRitualRequired.Should().BeFalse();
        service.PendingFocusBlockId.Should().BeNull();
        sessionChangedWith.Should().Be(dto);
        ritualChangedWith.Should().BeFalse();
    }

    [Fact]
    public void SetRitualRequired_True_SetsPendingBlockIdAndRaisesEvent()
    {
        var service = CreateService();
        var blockId = Guid.NewGuid();
        bool? raisedWith = null;
        service.RitualRequirementChanged += (_, required) => raisedWith = required;

        service.SetRitualRequired(true, blockId);

        service.IsRitualRequired.Should().BeTrue();
        service.PendingFocusBlockId.Should().Be(blockId);
        raisedWith.Should().BeTrue();
    }

    [Fact]
    public void SetRitualRequired_SameValueTwiceInARow_RaisesEventOnlyOnce()
    {
        var service = CreateService();
        var raiseCount = 0;
        service.SetRitualRequired(true, Guid.NewGuid());
        service.RitualRequirementChanged += (_, _) => raiseCount++;

        service.SetRitualRequired(true, Guid.NewGuid());

        raiseCount.Should().Be(0);
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
    public void Clear_ResetsEverythingAndRaisesBothEvents()
    {
        var service = CreateService();
        service.SetActive(new SessionDto { Id = Guid.NewGuid() });
        SessionDto? sessionChangedWith = new(); // sentinel, will be overwritten to null
        bool? ritualChangedWith = null;
        service.SessionChanged += (_, dto) => sessionChangedWith = dto;
        service.RitualRequirementChanged += (_, required) => ritualChangedWith = required;

        service.Clear();

        service.ActiveSession.Should().BeNull();
        service.IsRitualRequired.Should().BeFalse();
        service.PendingFocusBlockId.Should().BeNull();
        sessionChangedWith.Should().BeNull();
        ritualChangedWith.Should().BeFalse();
    }
}
