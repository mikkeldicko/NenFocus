using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class EndSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockedAttemptTracker _attemptTracker = Substitute.For<IBlockedAttemptTracker>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public EndSessionCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _attemptTracker.GetAttemptsForSessionAsync(Arg.Any<Guid>()).Returns([]);
    }

    private EndSessionCommandHandler CreateHandler()
        => new(_sessions, _profiles, _plannedBlocks, _attemptTracker, _clock);

    private static Session CreateActiveSession(Guid? originatingBlockId = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            originatingBlockId is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(Now.AddMinutes(-10));
        return session;
    }

    [Fact]
    public async Task HandleAsync_SessionNotFound_Fails()
    {
        _sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_SessionNotStarted_Fails()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AlreadyCompleted_Fails()
    {
        var session = CreateActiveSession();
        session.Complete(Now);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_OnBreak_IsAllowedToEnd()
    {
        var onBreakSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous, breakSettings: new BreakSettings(25, 5));
        onBreakSession.Start(Now.AddMinutes(-10));
        onBreakSession.StartBreak(Now.AddMinutes(-2));
        _sessions.GetByIdAsync(onBreakSession.Id, Arg.Any<CancellationToken>()).Returns(onBreakSession);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = onBreakSession.Id });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CompletesSessionAndReturnsPostSessionSummary()
    {
        var profileId = Guid.NewGuid();
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _profiles.GetByIdAsync(session.BlockProfileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(session.BlockProfileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        result.PostSession.Should().NotBeNull();
        result.PostSession!.ProfileName.Should().Be("Deep Work");
        result.PostSession.Goal.Should().Be("Deep work");
        session.Status.Should().Be(SessionStatus.Completed);
        await _sessions.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_GroupsBlockedAttemptsByResourceAndType()
    {
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _attemptTracker.GetAttemptsForSessionAsync(session.Id).Returns(
        [
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, Now),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, Now),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "facebook.com", BlockedAttemptType.Website, Now)
        ]);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.BlockedAppAttempts.Should().ContainKey("chrome").WhoseValue.Should().Be(2);
        result.PostSession.BlockedWebsiteAttempts.Should().ContainKey("facebook.com").WhoseValue.Should().Be(1);
        result.PostSession.TotalBlockedAttempts.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_OriginatingBlockEndsLaterThanActualEnd_TrimsBlockToActualEnd()
    {
        var blockId = Guid.NewGuid();
        var session = CreateActiveSession(blockId);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var block = FocusBlock.Create(blockId, session.BlockProfileId, Now.AddMinutes(-10), Now.AddHours(1));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        block.End.Should().Be(Now);
        await _plannedBlocks.Received(1).SaveAsync(block, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_OriginatingBlockAlreadyEndedNaturally_DoesNotTrim()
    {
        var blockId = Guid.NewGuid();
        var session = CreateActiveSession(blockId);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var block = FocusBlock.Create(blockId, session.BlockProfileId, Now.AddMinutes(-10), Now);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        await _plannedBlocks.DidNotReceive().SaveAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_ReturnsBlankProfileName()
    {
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _profiles.GetByIdAsync(session.BlockProfileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ProfileName.Should().BeEmpty();
    }
}
