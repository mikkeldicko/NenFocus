using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractBonusFocusHoursQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    private readonly IPlannedFocusHoursCalculator _calculator = new PlannedFocusHoursCalculator();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractBonusFocusHoursQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private GetNenContractBonusFocusHoursQueryHandler CreateHandler() => new(_contracts, _blocks, _sessions, _classifier, _calculator, _clock);

    private static NenContract ActiveContract(WorkHours? workHoursPolicy = null) => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None(), workHours: workHoursPolicy),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsEmpty()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockWithinContractRange_CreditsAsBonus()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1))); // no ContractId — Bonus
        block.MarkRitualCompleted(At.Utc(day));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.CreditedWindow(block.Scheduled, Arg.Any<Moment?>(), Arg.Any<Moment?>(), 0)
            .Returns((TimeSlot?)block.Scheduled);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.GetValueOrDefault(DateOnly.FromDateTime(day)).Should().Be(1.0);
    }

    [Fact]
    public async Task HandleAsync_BlockAlreadyLinkedToAContract_ExcludedFromBonus()
    {
        // Committed/redemption blocks (ContractId set) are counted by
        // GetNenContractCompletedFocusHoursQuery instead — never double-counted here.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1)), contractId: ContractId);
        block.MarkRitualCompleted(At.Utc(day));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
        _classifier.DidNotReceiveWithAnyArgs().CreditedWindow(default!, default, default, default);
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockOutsideContractDateRange_Excluded()
    {
        // Unlinked and within the *repository's* returned range, but its Start falls
        // outside this specific contract's own [StartDate, EndDate] — belongs to no
        // contract's Bonus total, or possibly a different contract's.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var outsideDay = EndDate.AddDays(5).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(outsideDay, outsideDay.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockAfterWorkHoursCloseOnLastDay_Excluded()
    {
        // Same calendar day as EndDate, but after Work Hours close — under the old
        // date-only truncation this counted as Bonus; EndsAt correctly excludes it.
        var policy = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(policy));
        var afterClose = EndDate.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(afterClose, afterClose.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockBeforeWorkHoursOpenOnFirstDay_Excluded()
    {
        // Same calendar day as StartDate, but before Work Hours open — StartsAt correctly
        // excludes it, unlike the old date-only truncation.
        var policy = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(policy));
        var beforeOpen = StartDate.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(beforeOpen, beforeOpen.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_Excluded()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1)));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
