using FluentAssertions;
using NenFocus.Application.UseCases.Planner.DeleteBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// Deleting a planned block, run as the app runs it: real pipeline, real SQLite (Gap 110).
/// May it go? BlockChangeChecker.CheckDelete (Gap 93): a running block never; a contract's block
/// only when its contract doesn't bind it and it hasn't started — everything else is part of the
/// contract's record, even after the contract is over.
/// </summary>
public class DeleteBlockTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    public DeleteBlockTests() => Clock.SetNow(Now);

    private Task<DeleteBlockResult> DeleteAsync(Guid blockId) =>
        RunAsync(new DeleteBlockCommand { BlockId = blockId });

    private static FocusBlock Focus(DateTime from, DateTime to, ContractMembership? membership = null) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(from, to), membership: membership);

    private static BackgroundBlock Background(DateTime from, DateTime to) =>
        BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(from, to));

    private async Task ShouldBeDeleted(Block block)
    {
        await SeedAsync(block);

        var result = await DeleteAsync(block.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<Block>(block.Id)).Should().BeNull();
    }

    private async Task<DeleteBlockResult> ShouldBeKept(Block block)
    {
        await SeedAsync(block);

        var result = await DeleteAsync(block.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<Block>(block.Id)).Should().NotBeNull();
        return result;
    }

    [Fact]
    public Task AFutureBlock_IsRemoved() =>
        ShouldBeDeleted(Focus(Now.AddHours(1), Now.AddHours(2)));

    [Fact]
    public async Task AlreadyGone_Succeeds()
    {
        // Gap 110: removing is idempotent.
        var result = await DeleteAsync(Guid.NewGuid());

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task EmptyBlockId_IsRefused()
    {
        var result = await DeleteAsync(Guid.Empty);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public Task AnEndedFocusBlockOutsideAnyContract_IsRemoved() =>
        // Only contract membership protects an ended block, so this one goes from its snapshot view.
        ShouldBeDeleted(Focus(Now.AddHours(-3), Now.AddHours(-1)));

    [Fact]
    public Task AnEndedBackgroundBlockOutsideAnyContract_IsRemoved() =>
        ShouldBeDeleted(Background(Now.AddHours(-3), Now.AddHours(-1)));

    [Fact]
    public async Task AMissedFocusBlockOutsideAnyContract_IsRemoved()
    {
        var block = Focus(Now.AddHours(-3), Now.AddHours(-1));
        block.Outcome(At.Utc(Now)).Should().Be(FocusBlockOutcome.Missed); // the "missed", not "completed", case

        await ShouldBeDeleted(block);
    }

    [Fact]
    public async Task ARunningBackgroundBlock_IsKept_AndSaysItIsRunning()
    {
        // Gap 88 phase 0: a started block runs to its end — it can't be deleted mid-run.
        var result = await ShouldBeKept(Background(Now.AddHours(-1), Now.AddHours(1)));

        result.ErrorMessage.Should().Be("This block is running and cannot be deleted until it ends.");
    }

    [Fact]
    public Task ARunningFocusBlockAwaitingItsRitual_IsKept() =>
        ShouldBeKept(Focus(Now.AddMinutes(-5), Now.AddHours(1)));

    [Fact]
    public Task ABlockStartingExactlyNow_IsKept() =>
        ShouldBeKept(Focus(Now, Now.AddHours(1)));

    [Fact]
    public async Task TheRunningSessionsBlock_IsKept()
    {
        // Was its own rule; a planned session only runs inside its block, so the running-block rule covers it.
        var block = Focus(Now.AddHours(-1), Now.AddHours(1));
        block.MarkRitualCompleted(At.Utc(Now.AddHours(-1)));

        await ShouldBeKept(block);
    }

    // ── A contract's blocks ──────────────────────────────────────────────────

    /// <summary>A Lockdown contract over <paramref name="from"/>..<paramref name="to"/>, sealed on its first day (so Active from then), stored.</summary>
    private async Task<NenContract> ContractAsync(DateOnly from, DateOnly to, bool bonusBlocksFollowStrictness = false, bool completed = false)
    {
        var rules = TestRules.Of(StrictnessRule.Locked, bonusBlocksFollowStrictness: bonusBlocksFollowStrictness);
        var contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(from, to, rules.FloorRule),
            new Dictionary<DateOnly, double>(), At.Utc(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), At.UtcZone);
        if (completed) contract.Complete(At.Utc(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        await SeedAsync(contract);
        return contract;
    }

    /// <summary>In effect: today and the next seven days.</summary>
    private Task<NenContract> ContractInEffect(bool bonusBlocksFollowStrictness = false) =>
        ContractAsync(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), bonusBlocksFollowStrictness);

    [Fact]
    public async Task AFutureCommittedBlock_IsKept()
    {
        var contract = await ContractInEffect();

        await ShouldBeKept(Focus(Now.AddHours(1), Now.AddHours(2), TestContracts.Committed(contract.Id)));
    }

    [Fact]
    public async Task AnEndedCommittedBlock_IsKept_EvenAfterItsContractIsOver()
    {
        // Membership protection is permanent, so a contract's history never silently changes.
        var today = DateOnly.FromDateTime(Now);
        var contract = await ContractAsync(today.AddDays(-7), today.AddDays(-2), completed: true);
        contract.Status.Should().Be(NenContractStatus.Completed);

        await ShouldBeKept(Focus(Now.AddDays(-3), Now.AddDays(-3).AddHours(1), TestContracts.Committed(contract.Id)));
    }

    // ── Bonus blocks: the contract's rules decide (Gap 93) ─────────────────────

    [Fact]
    public async Task AFutureFreeBonusBlock_IsRemoved_EvenUnderLockdown()
    {
        var contract = await ContractInEffect(bonusBlocksFollowStrictness: false);

        await ShouldBeDeleted(Focus(Now.AddHours(1), Now.AddHours(2), contract.MembershipForPlacedBlock()));
    }

    [Fact]
    public async Task ABindingBonusBlock_IsKept()
    {
        var contract = await ContractInEffect(bonusBlocksFollowStrictness: true);

        var result = await ShouldBeKept(Focus(Now.AddHours(1), Now.AddHours(2), contract.MembershipForPlacedBlock()));

        result.ErrorMessage.Should().Be("Cannot delete a block that belongs to a Nen Contract.");
    }

    [Fact]
    public async Task AnEndedFreeBonusBlock_IsKept()
    {
        // Its Bonus hours are part of the contract's recap — deleting it would rewrite that.
        var contract = await ContractInEffect(bonusBlocksFollowStrictness: false);

        await ShouldBeKept(Focus(Now.AddHours(-3), Now.AddHours(-1), contract.MembershipForPlacedBlock()));
    }
}
