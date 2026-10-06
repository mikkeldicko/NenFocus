using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

/// <summary>
/// Drafts only (Gap 95): the Contract Wizard's live planned hours. A sealed contract's days are
/// NenContract.DayStatuses, pinned in NenContractDayStatusTests.
/// </summary>
public class ComputePlannedFocusHoursQueryHandlerTests
{
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly ISystemClock _clock = TestClock.Create();

    public ComputePlannedFocusHoursQueryHandlerTests()
    {
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
    }

    private ComputePlannedFocusHoursQueryHandler CreateHandler() => new(_calculator, _clock);

    [Fact]
    public async Task HandleAsync_PassesTheDraftsAndTheUsersTimeZone_ToTheCalculator()
    {
        // Gap 88 phase 4: the calculator decides "which local day" from the time zone.
        var draftStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var draftEnd = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        var userTimeZone = _clock.TimeZone;

        await CreateHandler().HandleAsync(new ComputePlannedFocusHoursQuery([At.Slot(draftStart, draftEnd)]), CancellationToken.None);

        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.SequenceEqual(new[] { new TimeSlot(At.Utc(draftStart), At.Utc(draftEnd)) })),
            userTimeZone);
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverTheCalculatorComputes()
    {
        var expected = new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 1)] = 2 };
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(new ComputePlannedFocusHoursQuery([]), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }
}
