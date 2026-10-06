using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputePlannedFocusHoursQueryHandlerTests
{
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly ISystemClock _clock = TestClock.Create();
    private static readonly Guid ContractId = Guid.NewGuid();

    public ComputePlannedFocusHoursQueryHandlerTests()
    {
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
    }

    private ComputePlannedFocusHoursQueryHandler CreateHandler() => new(_blocks, _calculator, _clock);

    [Fact]
    public async Task HandleAsync_NoContractId_DoesNotQueryRepository()
    {
        var draftStart = new DateTime(2026, 1, 1, 9, 0, 0);
        var draftEnd = new DateTime(2026, 1, 1, 11, 0, 0);

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, [At.Slot(draftStart, draftEnd)]), CancellationToken.None);

        await _blocks.DidNotReceive().GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.SequenceEqual(new[] { new TimeSlot(At.Utc(draftStart), At.Utc(draftEnd)) })),
            Arg.Any<UserTimeZone>());
    }

    [Fact]
    public async Task HandleAsync_BonusBlockOfTheContract_AddsNothingToPlannedHours()
    {
        // Gap 97: planned hours are measured against the floor, and a Bonus block never counts toward it.
        var committed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0)), membership: TestContracts.Committed(ContractId));
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 2, 11, 0, 0), new DateTime(2026, 1, 2, 12, 0, 0)), membership: TestContracts.Redemption(ContractId));
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 2, 14, 0, 0), new DateTime(2026, 1, 2, 16, 0, 0)), membership: TestContracts.Bonus(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[committed, redemption, bonus]);

        await CreateHandler().HandleAsync(new ComputePlannedFocusHoursQuery(ContractId, []), CancellationToken.None);

        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.Count() == 2 && r.Contains(committed.Scheduled) && r.Contains(redemption.Scheduled)),
            Arg.Any<UserTimeZone>());
    }

    [Fact]
    public async Task HandleAsync_WithContractId_MergesPersistedFocusBlocksWithDrafts()
    {
        var persistedFocus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0)), membership: TestContracts.Committed(ContractId));
        var persistedBackground = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(new DateTime(2026, 1, 2, 12, 0, 0), new DateTime(2026, 1, 2, 13, 0, 0)), membership: TestContracts.Committed(ContractId));

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[persistedFocus, persistedBackground]);

        var draftStart = new DateTime(2026, 1, 3, 9, 0, 0);
        var draftEnd = new DateTime(2026, 1, 3, 10, 0, 0);

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId, [At.Slot(draftStart, draftEnd)]), CancellationToken.None);

        // Only the Focus block's range is included — Background hours never count toward the floor.
        var draft = new TimeSlot(At.Utc(draftStart), At.Utc(draftEnd));
        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.Count() == 2 && r.Contains(persistedFocus.Scheduled) && r.Contains(draft)),
            Arg.Any<UserTimeZone>());
    }

    [Fact]
    public async Task HandleAsync_PassesRealTimeAndTheUsersTimeZone_ToTheCalculator()
    {
        // Gap 88 phase 4: the calculator decides "which local day" from the time zone — the
        // query no longer converts to local first.
        var draftStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var draftEnd = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        var userTimeZone = _clock.TimeZone;

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, [At.Slot(draftStart, draftEnd)]), CancellationToken.None);

        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.SequenceEqual(new[] { new TimeSlot(At.Utc(draftStart), At.Utc(draftEnd)) })),
            userTimeZone);
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverTheCalculatorComputes()
    {
        var expected = new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 1)] = 2 };
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, []), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }
}
