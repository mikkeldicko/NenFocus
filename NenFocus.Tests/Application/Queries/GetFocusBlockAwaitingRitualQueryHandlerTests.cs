using FluentAssertions;
using TestAt = NenFocus.Tests.At;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;
using NenFocus.Domain.Entities;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetFocusBlockAwaitingRitualQueryHandlerTests
{
    private static readonly DateTime At = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    private GetFocusBlockAwaitingRitualQueryHandler CreateHandler() => new(_blocks, _sessions);

    private void BlocksAre(params PlannedBlock[] blocks) =>
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns(blocks);

    private void SessionRunning(bool running) =>
        _sessions.HasActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(running);

    private static FocusBlock ActiveFocusBlock() =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddMinutes(-5), At.AddHours(1)));

    [Fact]
    public async Task HandleAsync_NoBlocks_ReturnsNull()
    {
        BlocksAre();
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ActiveFocusBlockAwaitingRitualAndNoSession_ReturnsItsId()
    {
        var block = ActiveFocusBlock();
        BlocksAre(block);
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().Be(block.Id);
    }

    [Fact]
    public async Task HandleAsync_SessionAlreadyRunning_ReturnsNull()
    {
        BlocksAre(ActiveFocusBlock());
        SessionRunning(true);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_RitualAlreadyCompleted_ReturnsNull()
    {
        // The end-of-block race: still inside the window at `At`, session just completed,
        // nothing running — must not re-prompt the Ritual for a block that already had one.
        var block = ActiveFocusBlock();
        block.MarkRitualCompleted(TestAt.Utc(At.AddMinutes(-5)));
        BlocksAre(block);
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OnlyBackgroundBlockActive_ReturnsNull()
    {
        BlocksAre(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddMinutes(-5), At.AddHours(1))));
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BlockReturnedByLooseFetchButAlreadyEnded_ReturnsNull()
    {
        // The repository fetch is buffered by ±1s (SQLite datetime slop); the entity is the
        // precise authority, so a block that ended a moment before `At` must not qualify.
        BlocksAre(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddHours(-1), At.AddSeconds(-1))));
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NewBlockInEarlyEndedBlocksFreedTail_ReturnsTheNewBlock()
    {
        // Moved from PolicyEngineTests (Gap 32's live bug, Gap 62's move): a block whose
        // session ended early frees the rest of its window; a redemption block scheduled
        // into that freed tail must get its Ritual, not be hidden behind the stale block.
        var staleBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddHours(-1), At.AddHours(1)));
        staleBlock.MarkRitualCompleted(TestAt.Utc(At.AddHours(-1)));
        staleBlock.MarkSessionEnded(TestAt.Utc(At.AddMinutes(-2)));
        var newBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddMinutes(-1), At.AddMinutes(30)));
        BlocksAre(staleBlock, newBlock);
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().Be(newBlock.Id);
    }

    [Fact]
    public async Task HandleAsync_FocusAndBackgroundBothActive_ReturnsTheFocusBlock()
    {
        var focusBlock = ActiveFocusBlock();
        BlocksAre(
            BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At.AddMinutes(-5), At.AddHours(1))),
            focusBlock);
        SessionRunning(false);

        var result = await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        result.Should().Be(focusBlock.Id);
    }

    [Fact]
    public async Task HandleAsync_FetchesBlocksAroundTheGivenInstant()
    {
        BlocksAre();
        SessionRunning(false);

        await CreateHandler().HandleAsync(new GetFocusBlockAwaitingRitualQuery(TestAt.Utc(At)));

        await _blocks.Received(1).GetForRangeAsync(new TimeSlot(TestAt.Utc(At.AddSeconds(-1)), TestAt.Utc(At.AddSeconds(1))), Arg.Any<CancellationToken>());
    }
}
