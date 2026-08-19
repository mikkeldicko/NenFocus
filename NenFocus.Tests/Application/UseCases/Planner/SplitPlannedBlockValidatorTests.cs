using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class SplitPlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    // Original block: 14:00-17:00 UTC, well into the future relative to Now.
    private static readonly DateTime OriginalStart = new(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OriginalEnd = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SplitPlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private SplitPlannedBlockValidator CreateValidator() => new(_blocks, _sessions, _contracts, _clock);

    private static SplitPlannedBlockCommand ValidCommand(Guid blockId) => new()
    {
        OriginalBlockId = blockId,
        FirstBlockStart = OriginalStart,
        FirstBlockEnd = OriginalStart.AddHours(2),
        SecondBlockStart = OriginalStart.AddHours(2),
        SecondBlockEnd = OriginalEnd
    };

    private static NenContract SealedContract(StrictnessRule rule, Guid id) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"),
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30),
            rule, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now);

    private void SetUpDailyFlexBlock(Guid blockId, Guid contractId)
    {
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd, contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyOriginalBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockNotFound_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockHasNoContract_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockdownContract_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd, contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetContract_IsInvalid()
    {
        // Only Daily Flexibility allows splitting — Period Time Budget doesn't get the
        // same free pass AllowsBlockTimeChange gives it for ordinary moves.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd, contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CancelledBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd, contractId: contractId);
        block.MarkCancelled(Now);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockHasSession_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PastBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var pastStart = Now.AddHours(-3);
        var pastEnd = Now.AddHours(-1);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), pastStart, pastEnd, contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            FirstBlockStart = pastStart,
            FirstBlockEnd = pastStart.AddHours(1),
            SecondBlockStart = pastStart.AddHours(1),
            SecondBlockEnd = pastEnd
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SplitResultsOnDifferentDay_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        var command = ValidCommand(blockId) with
        {
            SecondBlockStart = OriginalStart.AddHours(2).AddDays(1),
            SecondBlockEnd = OriginalEnd.AddDays(1)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ResultingBlocksOverlapEachOther_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        // First: 14:00-16:00, Second: 15:00-17:00 — overlaps First by an hour.
        var command = ValidCommand(blockId) with
        {
            SecondBlockStart = OriginalStart.AddHours(1),
            SecondBlockEnd = OriginalEnd
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ResultingBlockOverlapsExistingBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        _blocks.GetOverlappingAsync(
                ValidCommand(blockId).FirstBlockStart, ValidCommand(blockId).FirstBlockEnd,
                Now, true, blockId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), OriginalStart, OriginalStart.AddHours(2))]);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_TotalDurationMismatch_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        // Original is 3h (14:00-17:00); split only adds up to 2h30m, dropping 30 minutes.
        var command = ValidCommand(blockId) with { SecondBlockEnd = OriginalStart.AddHours(2).AddMinutes(30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FirstBlockEndBeforeStart_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var command = ValidCommand(blockId) with { FirstBlockEnd = OriginalStart.AddMinutes(-30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SecondBlockEndsExactlyAtMidnight_SameDayIsValid()
    {
        // Original block is 22:00-24:00 so the split's total duration (1h + 1h) still
        // matches it exactly; the second half ends exactly at midnight — no actual
        // presence on the next calendar day. localDay is 2 days out so every wall-clock
        // hour on it converts to a UTC instant safely after Now regardless of the test
        // machine's timezone offset.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var localDay = Now.ToLocalTime().Date.AddDays(2);
        DateTime AtLocal(double hours) => DateTime.SpecifyKind(localDay.AddHours(hours), DateTimeKind.Local).ToUniversalTime();

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), AtLocal(22), AtLocal(24), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            FirstBlockStart = AtLocal(22),
            FirstBlockEnd = AtLocal(23),
            SecondBlockStart = AtLocal(23),
            SecondBlockEnd = AtLocal(24)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlock_UsesNonRitualOverlapCheck()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(), OriginalStart, OriginalEnd, contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
        await _blocks.Received().GetOverlappingAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), false, blockId, Arg.Any<CancellationToken>());
    }
}
