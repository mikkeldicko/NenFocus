using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Application.Services;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class BreakTransitionResponderTests
{
    private static readonly DateTime At = new(2026, 1, 1, 17, 25, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();

    private BreakTransitionResponder CreateResponder() => new(_synchronizer, _notifier);

    private static BreakTransition Started(SessionPhaseKind kind = SessionPhaseKind.ShortBreak) =>
        new(BreakTransitionKind.Started, kind, TimeSpan.FromMinutes(25));

    private static BreakTransition Ended() =>
        new(BreakTransitionKind.Ended, SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(30));

    [Fact]
    public async Task RespondAsync_BreakStarted_SyncsEnforcementThenReportsBreakStarted()
    {
        await CreateResponder().RespondAsync([Started()], At, keepBlockingDuringBreaks: false);

        Received.InOrder(() =>
        {
            _synchronizer.SyncAsync(At, Arg.Any<CancellationToken>());
            _notifier.NotifyAsync(new BreakStarted(IsLong: false, KeepsBlocking: false), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RespondAsync_LongBreakStarted_ReportsLongBreak()
    {
        await CreateResponder().RespondAsync([Started(SessionPhaseKind.LongBreak)], At, keepBlockingDuringBreaks: false);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(IsLong: true, KeepsBlocking: false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_BreakStarted_KeepBlocking_ReportsItKeepsBlocking()
    {
        await CreateResponder().RespondAsync([Started()], At, keepBlockingDuringBreaks: true);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(IsLong: false, KeepsBlocking: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_BreakEnded_SyncsAndReportsBreakEnded()
    {
        await CreateResponder().RespondAsync([Ended()], At, keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_WholeBreakCrossedAtOnce_SyncsOnceAndOnlyAnnouncesWhereTheSessionIsNow()
    {
        // Laptop slept through the entire break: "Break time" would be stale.
        await CreateResponder().RespondAsync([Started(), Ended()], At, keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(At, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(Arg.Any<NenNotification>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_NoTransitions_DoesNothing()
    {
        await CreateResponder().RespondAsync([], At, keepBlockingDuringBreaks: false);

        await _synchronizer.DidNotReceiveWithAnyArgs().SyncAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!);
    }
}
