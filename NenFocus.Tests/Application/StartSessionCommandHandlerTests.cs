using FluentAssertions;
using NSubstitute;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Application;

/// <summary>
/// Unit tests for StartSessionCommandHandler.
/// Demonstrates the NSubstitute mocking pattern used across Application layer tests.
/// </summary>
public class StartSessionCommandHandlerTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();

    private StartSessionCommandHandler CreateHandler() =>
        new(_sessions, _plannedBlocks, _sessionState);

    private static StartSessionCommand ValidCommand(int durationMinutes = 25) => new()
    {
        Goal = "Deep work",
        DurationMinutes = durationMinutes,
        ProfileId = Guid.NewGuid(),
        BreakSettings = null,
        TemporaryBlockedApps = [],
        TemporaryBlockedSites = []
    };

    [Fact]
    public async Task HandleAsync_NoConflicts_ReturnsSuccess()
    {
        _plannedBlocks
            .GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task HandleAsync_NoConflicts_PersistsSession()
    {
        _plannedBlocks
            .GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        await _sessions.Received(1).SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoConflicts_SetsActiveSessionState()
    {
        _plannedBlocks
            .GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        _sessionState.Received(1).SetActive(Arg.Any<SessionDto>());
    }

    [Fact]
    public async Task HandleAsync_ConflictingFocusBlock_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var conflictingBlock = FocusBlock.Create(
            sessionId: Guid.Empty,
            blockProfileId: Guid.NewGuid(),
            type: BlockType.Focus,
            start: now.AddMinutes(10),
            end: now.AddMinutes(70),
            isPlanned: true);

        _plannedBlocks
            .GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<PlannedBlock> { conflictingBlock });

        var result = await CreateHandler().HandleAsync(ValidCommand(durationMinutes: 30), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleAsync_ConflictingFocusBlock_DoesNotPersist()
    {
        var now = DateTime.UtcNow;
        var conflictingBlock = FocusBlock.Create(
            sessionId: Guid.Empty,
            blockProfileId: Guid.NewGuid(),
            type: BlockType.Focus,
            start: now.AddMinutes(10),
            end: now.AddMinutes(70),
            isPlanned: true);

        _plannedBlocks
            .GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<PlannedBlock> { conflictingBlock });

        await CreateHandler().HandleAsync(ValidCommand(durationMinutes: 30), CancellationToken.None);

        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
