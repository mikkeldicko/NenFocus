using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class DeletePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public DeletePlannedBlockValidatorTests()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);
        _clock.SetNow(Now);
    }

    private DeletePlannedBlockValidator CreateValidator() => new(_blocks, _contracts, _clock, new BlockChangeChecker(new PlannedFocusHoursCalculator()));

    [Fact]
    public async Task Validate_ValidBlockId_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlockAlreadyEndedNotContractLinked_IsValid()
    {
        // Ended blocks are no longer permanently protected — only contract-linkage protects
        // a block from deletion now, so this one (no ContractId) is deletable from its
        // Planner snapshot view.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlockAlreadyEndedNotContractLinked_IsValid()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MissedFocusBlockNotContractLinked_IsValid()
    {
        // Ritual never completed, window closed — deletable from the snapshot view just
        // like a completed one, as long as it's not tied to a Nen Contract.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        block.RitualCompletedAt.Should().BeNull(); // confirms this is the "missed", not "completed", case
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FutureBlock_IsValid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(1), Now.AddHours(2)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RunningBackgroundBlock_IsInvalid()
    {
        // Gap 88 phase 0: a started block runs to its end — it can't be deleted mid-run.
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-1), Now.AddHours(1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RunningFocusBlockAwaitingRitual_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddMinutes(-5), Now.AddHours(1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockStartingExactlyNow_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RunningSessionsOriginatingBlock_IsInvalid()
    {
        // Was its own rule (the active session's originating block); a planned session only
        // runs inside its block's window, so the running-block rule covers it.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-1), Now.AddHours(1)));
        block.MarkRitualCompleted(At.Utc(Now.AddHours(-1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }
    [Fact]
    public async Task Validate_BlockUnderActiveContract_IsInvalid()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockUnderConcludedContract_IsStillInvalid()
    {
        // Contract-linkage protection is permanent — once a block's ContractId is set, it
        // stays undeletable forever, even after the contract itself has concluded, so a
        // contract's historical stats can never silently change.
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-1)), membership: TestContracts.Committed(contractId));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    // ── Bonus blocks: the contract's rules decide (Gap 93) ─────────────────────

    private NenContract ContractInEffect(bool bonusBlocksFollowStrictness)
    {
        var rules = TestRules.Of(StrictnessRule.Locked, bonusBlocksFollowStrictness: bonusBlocksFollowStrictness);
        var contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), rules.FloorRule),
            null, new Dictionary<DateOnly, double>(), At.Utc(Now.Date), At.UtcZone);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        return contract;
    }

    private FocusBlock BonusBlock(NenContract contract, DateTime from, DateTime to)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(from, to),
            membership: contract.MembershipForPlacedBlock(claimsDeficit: false));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        return block;
    }

    [Fact]
    public async Task Validate_AFutureFreeBonusBlock_IsValid_EvenUnderLockdown()
    {
        var block = BonusBlock(ContractInEffect(bonusBlocksFollowStrictness: false), Now.AddHours(1), Now.AddHours(2));

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ABindingBonusBlock_IsInvalid()
    {
        var block = BonusBlock(ContractInEffect(bonusBlocksFollowStrictness: true), Now.AddHours(1), Now.AddHours(2));

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Cannot delete a block that belongs to a Nen Contract.");
    }

    [Fact]
    public async Task Validate_AnEndedFreeBonusBlock_IsInvalid()
    {
        // Its Bonus hours are part of the contract's recap — deleting it would rewrite that.
        var block = BonusBlock(ContractInEffect(bonusBlocksFollowStrictness: false), Now.AddHours(-3), Now.AddHours(-1));

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ARunningBlock_SaysItIsRunning()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-1), Now.AddHours(1)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This block is running and cannot be deleted until it ends.");
    }
}
