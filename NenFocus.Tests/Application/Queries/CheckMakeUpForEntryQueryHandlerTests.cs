using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckMakeUpForEntry;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class CheckMakeUpForEntryQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid PresetId = Guid.NewGuid();
    private static readonly DateTime MissedBlockDate = new(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();

    private CheckMakeUpForEntryQueryHandler CreateHandler() => new(_blocks, _contracts, _deficits);

    private static FocusBlock MissedBlock() =>
        FocusBlock.Create(Guid.NewGuid(), PresetId, MissedBlockDate, MissedBlockDate.AddHours(2), contractId: ContractId);

    private static NenContract ContractWith(StrictnessRule rule) => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: DateOnly.FromDateTime(MissedBlockDate).AddDays(-1),
        endDate: DateOnly.FromDateTime(MissedBlockDate).AddDays(10),
        strictnessRule: rule,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: MissedBlockDate.AddDays(-1));

    private CheckMakeUpForEntryQuery Query(Guid missedBlockId, DateTime blockDate, double minutesCovered = 30, Guid? excludeBlockId = null) =>
        new(PresetId, blockDate, missedBlockId, minutesCovered, excludeBlockId);

    [Fact]
    public async Task HandleAsync_MissedBlockNotFound_ReturnsMissedBlockNotFound()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(Query(Guid.NewGuid(), MissedBlockDate), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.MissedBlockNotFound);
    }

    [Fact]
    public async Task HandleAsync_ReferencedBlockIsBackgroundBlock_ReturnsMissedBlockNotFound()
    {
        var background = BackgroundBlock.Create(Guid.NewGuid(), PresetId, MissedBlockDate, MissedBlockDate.AddHours(1), contractId: ContractId);
        _blocks.GetByIdAsync(background.Id, Arg.Any<CancellationToken>()).Returns(background);

        var result = await CreateHandler().HandleAsync(Query(background.Id, MissedBlockDate), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.MissedBlockNotFound);
    }

    [Fact]
    public async Task HandleAsync_ReferencedBlockHasNoContract_ReturnsMissedBlockNotFound()
    {
        var uncontracted = FocusBlock.Create(Guid.NewGuid(), PresetId, MissedBlockDate, MissedBlockDate.AddHours(1));
        _blocks.GetByIdAsync(uncontracted.Id, Arg.Any<CancellationToken>()).Returns(uncontracted);

        var result = await CreateHandler().HandleAsync(Query(uncontracted.Id, MissedBlockDate), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.MissedBlockNotFound);
    }

    [Fact]
    public async Task HandleAsync_PresetMismatch_ReturnsPresetMismatch()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);

        var query = Query(missed.Id, MissedBlockDate) with { PresetId = Guid.NewGuid() };
        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.PresetMismatch);
    }

    [Fact]
    public async Task HandleAsync_LockedContract_ReturnsStrictnessDoesNotAllowMakeUp()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.Locked));

        var result = await CreateHandler().HandleAsync(Query(missed.Id, MissedBlockDate), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.StrictnessDoesNotAllowMakeUp);
    }

    [Fact]
    public async Task HandleAsync_WrongDay_ReturnsWrongDay()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.DailyFlexibility));

        var result = await CreateHandler().HandleAsync(Query(missed.Id, MissedBlockDate.AddDays(1)), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.WrongDay);
    }

    [Fact]
    public async Task HandleAsync_SameDay_PassesTheDayCheck()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.DailyFlexibility));
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[
                new FocusBlockDeficitDto { FocusBlockId = missed.Id, RemainingDeficitMinutes = 120 }
            ]);

        var sameDayLaterHour = MissedBlockDate.AddHours(5);
        var result = await CreateHandler().HandleAsync(Query(missed.Id, sameDayLaterHour, 30), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.Valid);
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudget_AnyDayPassesTheDayCheck()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.PeriodTimeBudget));
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[
                new FocusBlockDeficitDto { FocusBlockId = missed.Id, RemainingDeficitMinutes = 120 }
            ]);

        var result = await CreateHandler().HandleAsync(Query(missed.Id, MissedBlockDate.AddDays(5), 30), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.Valid);
    }

    [Fact]
    public async Task HandleAsync_ExceedsRemainingDeficit_ReturnsExceedsRemainingDeficit()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.PeriodTimeBudget));
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[
                new FocusBlockDeficitDto { FocusBlockId = missed.Id, RemainingDeficitMinutes = 20 }
            ]);

        var result = await CreateHandler().HandleAsync(Query(missed.Id, MissedBlockDate, 30), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.ExceedsRemainingDeficit);
    }

    [Fact]
    public async Task HandleAsync_BlockNotInDeficitList_TreatsRemainingAsZero()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.PeriodTimeBudget));
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]); // e.g. already fully covered, or not actually missed

        var result = await CreateHandler().HandleAsync(Query(missed.Id, MissedBlockDate, 1), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.ExceedsRemainingDeficit);
    }

    [Fact]
    public async Task HandleAsync_ExcludeBlockId_AddsBackItsOwnPriorContributionBeforeComparing()
    {
        var missed = MissedBlock();
        _blocks.GetByIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(missed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ContractWith(StrictnessRule.PeriodTimeBudget));
        // Query already reflects this make-up block's OLD 30-minute contribution as "covered".
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[
                new FocusBlockDeficitDto { FocusBlockId = missed.Id, RemainingDeficitMinutes = 10 }
            ]);

        var editingBlock = FocusBlock.Create(Guid.NewGuid(), PresetId, MissedBlockDate.AddDays(1), MissedBlockDate.AddDays(1).AddHours(1),
            contractId: ContractId, makeUpForEntries: [new MakeUpForEntry(missed.Id, 30)]);
        _blocks.GetByIdAsync(editingBlock.Id, Arg.Any<CancellationToken>()).Returns(editingBlock);

        // Proposing to raise it from 30 to 40 — without adding back the old 30, remaining
        // (10) would wrongly reject this; with it added back (10 + 30 = 40), it just fits.
        var result = await CreateHandler().HandleAsync(
            Query(missed.Id, MissedBlockDate, 40, excludeBlockId: editingBlock.Id), CancellationToken.None);

        result.Should().Be(MakeUpForEntryCheckResult.Valid);
    }
}
