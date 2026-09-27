using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class BreakTransitionHandlerTests
{
    private static readonly DateTime At = new(2026, 1, 1, 17, 25, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private BreakTransitionHandler CreateHandler() => new(_synchronizer, _notifications);

    private static BreakTransition Started(SessionPhaseKind kind = SessionPhaseKind.ShortBreak) =>
        new(BreakTransitionKind.Started, kind, TimeSpan.FromMinutes(25));

    private static BreakTransition Ended() =>
        new(BreakTransitionKind.Ended, SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(30));

    [Fact]
    public async Task HandleAsync_BreakStarted_SyncsEnforcementThenNotifiesBreakTime()
    {
        await CreateHandler().HandleAsync([Started()], At);

        Received.InOrder(() =>
        {
            _synchronizer.SyncAsync(At, Arg.Any<CancellationToken>());
            _notifications.NotifyAsync("Break time", Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_LongBreakStarted_SaysSo()
    {
        await CreateHandler().HandleAsync([Started(SessionPhaseKind.LongBreak)], At);

        await _notifications.Received(1).NotifyAsync("Long break time", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BreakEnded_NotifiesBreakOver()
    {
        await CreateHandler().HandleAsync([Ended()], At);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync("Break over", "Back to focus.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WholeBreakCrossedAtOnce_SyncsOnceAndOnlyAnnouncesWhereTheSessionIsNow()
    {
        // Laptop slept through the entire break: "Break time" would be stale.
        await CreateHandler().HandleAsync([Started(), Ended()], At);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync("Break over", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoTransitions_DoesNothing()
    {
        await CreateHandler().HandleAsync([], At);

        await _synchronizer.DidNotReceiveWithAnyArgs().SyncAsync(default);
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(default!, default!);
    }
}
