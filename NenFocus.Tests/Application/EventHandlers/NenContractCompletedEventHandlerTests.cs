using FluentAssertions;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class NenContractCompletedEventHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private NenContractCompletedEventHandler CreateHandler() => new(_contracts, _notifications);

    private static NenContract SealedContract(string name = "Focused Week") => NenContract.Seal(
        id: Guid.NewGuid(),
        name: name,
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_ContractExists_SendsNotificationWithContractName()
    {
        var contract = SealedContract("Study Month");
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        await CreateHandler().HandleAsync(new NenContractCompletedEvent(contract.Id, DateTime.UtcNow));

        await _notifications.Received(1).NotifyAsync(
            Arg.Any<string>(), Arg.Is<string>(body => body.Contains("Study Month")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractNotFound_SkipsNotification()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        await CreateHandler().HandleAsync(new NenContractCompletedEvent(Guid.NewGuid(), DateTime.UtcNow));

        await _notifications.DidNotReceive().NotifyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
