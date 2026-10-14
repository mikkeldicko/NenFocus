using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class RitualPromptTests
{
    private static readonly Moment Now = At.Utc(new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc));

    private readonly IQueryDispatcher _queries = Substitute.For<IQueryDispatcher>();

    private RitualPrompt CreatePrompt() => new(_queries);

    private void Awaiting(Guid? blockId) =>
        _queries.QueryAsync(Arg.Any<GetFocusBlockAwaitingRitualQuery>(), Arg.Any<CancellationToken>()).Returns(blockId);

    [Fact]
    public async Task RefreshAsync_AsksTheQueryAtTheGivenMoment()
    {
        Awaiting(null);

        await CreatePrompt().RefreshAsync(Now);

        await _queries.Received(1).QueryAsync(
            Arg.Is<GetFocusBlockAwaitingRitualQuery>(q => q.At == Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_BlockAwaiting_NamesItAndRaisesChanged()
    {
        var blockId = Guid.NewGuid();
        Awaiting(blockId);
        var prompt = CreatePrompt();
        Guid? raisedWith = null;
        prompt.Changed += (_, id) => raisedWith = id;

        await prompt.RefreshAsync(Now);

        prompt.PendingFocusBlockId.Should().Be(blockId);
        raisedWith.Should().Be(blockId);
    }

    [Fact]
    public async Task RefreshAsync_SameBlockStillAwaiting_DoesNotRaiseAgain()
    {
        var blockId = Guid.NewGuid();
        Awaiting(blockId);
        var prompt = CreatePrompt();
        await prompt.RefreshAsync(Now);
        var raised = 0;
        prompt.Changed += (_, _) => raised++;

        await prompt.RefreshAsync(Now + TimeSpan.FromSeconds(5));

        raised.Should().Be(0);
    }

    [Fact]
    public async Task RefreshAsync_BlockNoLongerAwaiting_StopsNamingItAndRaisesNull()
    {
        Awaiting(Guid.NewGuid());
        var prompt = CreatePrompt();
        await prompt.RefreshAsync(Now);
        Awaiting(null);
        Guid? raisedWith = Guid.NewGuid(); // sentinel, overwritten with null
        prompt.Changed += (_, id) => raisedWith = id;

        await prompt.RefreshAsync(Now + TimeSpan.FromSeconds(5));

        prompt.PendingFocusBlockId.Should().BeNull();
        raisedWith.Should().BeNull();
    }

    // Back-to-back blocks: the next one is simply named — no hand-off id. The previous block's
    // missed screen comes from the heartbeat's sweep, earlier in the same tick (D′).
    [Fact]
    public async Task RefreshAsync_NextBlockReplacesThePrevious_NamesTheNextOne()
    {
        Awaiting(Guid.NewGuid());
        var prompt = CreatePrompt();
        await prompt.RefreshAsync(Now);
        var next = Guid.NewGuid();
        Awaiting(next);
        Guid? raisedWith = null;
        prompt.Changed += (_, id) => raisedWith = id;

        await prompt.RefreshAsync(Now + TimeSpan.FromSeconds(5));

        prompt.PendingFocusBlockId.Should().Be(next);
        raisedWith.Should().Be(next);
    }

    [Fact]
    public async Task RefreshAsync_NothingAwaitingFromTheStart_RaisesNothing()
    {
        Awaiting(null);
        var prompt = CreatePrompt();
        var raised = false;
        prompt.Changed += (_, _) => raised = true;

        await prompt.RefreshAsync(Now);

        raised.Should().BeFalse();
    }
}
