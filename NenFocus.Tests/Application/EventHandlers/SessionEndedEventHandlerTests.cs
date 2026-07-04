using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionEndedEventHandlerTests
{
    private readonly IEnforcementService _enforcement = Substitute.For<IEnforcementService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();

    private SessionEndedEventHandler CreateHandler() => new(_enforcement, _notifications, _sessionState);

    [Fact]
    public async Task HandleAsync_ClearsSessionStateRemovesEnforcementAndNotifies()
    {
        await CreateHandler().HandleAsync(new SessionEndedEvent(Guid.NewGuid(), DateTime.UtcNow));

        _sessionState.Received(1).Clear();
        await _enforcement.Received(1).RemoveEnforcementAsync(Arg.Any<CancellationToken>());
        await _notifications.Received(1).CancelAllRemindersAsync(Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(
            Arg.Is<string>(t => t.Contains("complete")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClearsSessionStateBeforeRemovingEnforcement()
    {
        // Ordering matters: sessionState.Clear() must happen before enforcement removal
        // so ScheduledEnforcementService never observes a null ActiveSession while the
        // DB still shows the session as Active (see the handler's own comment).
        await CreateHandler().HandleAsync(new SessionEndedEvent(Guid.NewGuid(), DateTime.UtcNow));

        Received.InOrder(() =>
        {
            _sessionState.Received().Clear();
            _enforcement.Received().RemoveEnforcementAsync(Arg.Any<CancellationToken>());
        });
    }
}
