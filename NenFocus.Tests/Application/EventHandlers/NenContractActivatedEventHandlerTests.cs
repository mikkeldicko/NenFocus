using FluentAssertions;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class NenContractActivatedEventHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();

    private NenContractActivatedEventHandler CreateHandler() => new(_contracts, _notifier);

    private static NenContract SealedContract(string name = "Focused Week") => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: name,
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractExists_SendsNotificationWithContractName()
    {
        var contract = SealedContract("Study Month");
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        await CreateHandler().HandleAsync(new NenContractActivatedEvent(contract.Id, At.Utc(DateTime.UtcNow)));

        await _notifier.Received(1).NotifyAsync(new ContractActivated("Study Month"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractNotFound_SkipsNotification()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        await CreateHandler().HandleAsync(new NenContractActivatedEvent(Guid.NewGuid(), At.Utc(DateTime.UtcNow)));

        await _notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!);
    }
}
