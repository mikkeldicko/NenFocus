using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Application.Services;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class BreakTransitionHandlerTests
{
    private static readonly DateTime At = new(2026, 1, 1, 17, 25, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();

    private BreakTransitionHandler CreateHandler() => new(_synchronizer, _notifier);

    private static BreakTransition Started(SessionPhaseKind kind = SessionPhaseKind.ShortBreak) =>
        new(BreakTransitionKind.Started, kind, TimeSpan.FromMinutes(25));

    private static BreakTransition Ended() =>
        new(BreakTransitionKind.Ended, SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(30));

    [Fact]
    public async Task HandleAsync_BreakStarted_SyncsEnforcementThenReportsBreakStarted()
    {
        await CreateHandler().HandleAsync([Started()], At, keepBlockingDuringBreaks: false);

        Received.InOrder(() =>
        {
            _synchronizer.SyncAsync(At, Arg.Any<CancellationToken>());
            _notifier.NotifyAsync(new BreakStarted(IsLong: false, KeepsBlocking: false), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_LongBreakStarted_ReportsLongBreak()
    {
        await CreateHandler().HandleAsync([Started(SessionPhaseKind.LongBreak)], At, keepBlockingDuringBreaks: false);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(IsLong: true, KeepsBlocking: false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BreakStarted_KeepBlocking_ReportsItKeepsBlocking()
    {
        await CreateHandler().HandleAsync([Started()], At, keepBlockingDuringBreaks: true);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(IsLong: false, KeepsBlocking: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BreakEnded_SyncsAndReportsBreakEnded()
    {
        await CreateHandler().HandleAsync([Ended()], At, keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WholeBreakCrossedAtOnce_SyncsOnceAndOnlyAnnouncesWhereTheSessionIsNow()
    {
        // Laptop slept through the entire break: "Break time" would be stale.
        await CreateHandler().HandleAsync([Started(), Ended()], At, keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(Arg.Any<NenNotification>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoTransitions_DoesNothing()
    {
        await CreateHandler().HandleAsync([], At, keepBlockingDuringBreaks: false);

        await _synchronizer.DidNotReceiveWithAnyArgs().SyncAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!);
    }
}
