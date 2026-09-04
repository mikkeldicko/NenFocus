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
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractBonusFocusHoursQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private GetNenContractBonusFocusHoursQueryHandler CreateHandler() => new(_contracts, _blocks, _sessions, _classifier, _calculator, _clock);

    private static NenContract ActiveContract() => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.Locked,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

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
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(1)); // no ContractId — Bonus
        block.MarkRitualCompleted(day);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.CreditedWindow(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(block.Start, block.End));

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
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(1), contractId: ContractId);
        block.MarkRitualCompleted(day);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
        _classifier.DidNotReceiveWithAnyArgs().CreditedWindow(default, default, default, default, default);
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockOutsideContractDateRange_Excluded()
    {
        // Unlinked and within the *repository's* returned range, but its Start falls
        // outside this specific contract's own [StartDate, EndDate] — belongs to no
        // contract's Bonus total, or possibly a different contract's.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var outsideDay = EndDate.AddDays(5).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), outsideDay, outsideDay.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_Excluded()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
