using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class BreakStartedEventHandlerTests
{
    private readonly IEnforcementService _enforcement = Substitute.For<IEnforcementService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private BreakStartedEventHandler CreateHandler() => new(_enforcement, _notifications);

    [Fact]
    public async Task HandleAsync_RemovesEnforcementAndNotifiesWithBreakNumber()
    {
        await CreateHandler().HandleAsync(new BreakStartedEvent(Guid.NewGuid(), breakNumber: 2, DateTime.UtcNow));

        await _enforcement.Received(1).RemoveEnforcementAsync(Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(
            Arg.Any<string>(), Arg.Is<string>(body => body.Contains("Break #2")), Arg.Any<CancellationToken>());
    }
}
