using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Domain;

/// <summary>
/// Gap 93: what a block itself allows, from its own schedule and the clock — in a contract or
/// not. Not started: change and delete. Running: nothing. Ended: no change; deleting an ended
/// block is the contract's to refuse, if it has one.
/// </summary>
public class BlockOwnLockTests
{
    private static readonly DateTime Start = new(2026, 1, 6, 10, 0, 0, DateTimeKind.Utc);

    private static FocusBlock Focus() => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)));
    private static BackgroundBlock Background() => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)));

    [Theory]
    [InlineData(-1, ChangeOutcome.Allowed)]         // not started
    [InlineData(0, ChangeOutcome.BlockHasStarted)]  // at its start
    [InlineData(60, ChangeOutcome.BlockHasStarted)] // running
    [InlineData(120, ChangeOutcome.BlockHasStarted)] // ended
    public void CheckOwnChange_IsAllowedOnlyBeforeItStarts(int minutesAfterStart, ChangeOutcome expected)
    {
        Focus().CheckOwnChange(At.Utc(Start.AddMinutes(minutesAfterStart))).Outcome.Should().Be(expected);
        Background().CheckOwnChange(At.Utc(Start.AddMinutes(minutesAfterStart))).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, ChangeOutcome.Allowed)]        // not started
    [InlineData(0, ChangeOutcome.BlockIsRunning)]
    [InlineData(119, ChangeOutcome.BlockIsRunning)]
    [InlineData(120, ChangeOutcome.Allowed)]       // ended — at exactly its end
    public void CheckOwnDelete_IsRefusedOnlyWhileItRuns(int minutesAfterStart, ChangeOutcome expected)
    {
        Focus().CheckOwnDelete(At.Utc(Start.AddMinutes(minutesAfterStart))).Outcome.Should().Be(expected);
    }

    [Fact]
    public void ACancelledBlock_IsHistory_EvenBeforeItsStart()
    {
        // Cancelled only when its contract was broken — it is part of that contract's record.
        var block = Focus();
        block.MarkCancelled(At.Utc(Start.AddHours(-2)));

        block.CheckOwnChange(At.Utc(Start.AddHours(-1))).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
        block.CheckOwnSplit(At.Utc(Start.AddHours(-1))).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
    }

    [Fact]
    public void CheckOwnSplit_AFutureFocusBlock_Allowed()
    {
        Focus().CheckOwnSplit(At.Utc(Start.AddMinutes(-1))).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Fact]
    public void CheckOwnSplit_ABackgroundBlock_SplitNotOffered()
    {
        Background().CheckOwnSplit(At.Utc(Start.AddMinutes(-1))).Outcome.Should().Be(ChangeOutcome.SplitNotOffered);
    }

    [Fact]
    public void CheckOwnSplit_AStartedFocusBlock_BlockHasStarted()
    {
        Focus().CheckOwnSplit(At.Utc(Start)).Outcome.Should().Be(ChangeOutcome.BlockHasStarted);
    }
}
