using NenFocus.Application.Interfaces;
using TestAt = NenFocus.Tests.At;
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

    private static BreakTransition Started(BreakKind kind = BreakKind.ShortBreak) =>
        new(BreakTransitionKind.Started, kind, TimeSpan.FromMinutes(25));

    private static BreakTransition Ended() =>
        new(BreakTransitionKind.Ended, BreakKind.ShortBreak, TimeSpan.FromMinutes(30));

    [Fact]
    public async Task RespondAsync_BreakStarted_SyncsEnforcementThenReportsBreakStarted()
    {
        await CreateResponder().RespondAsync([Started()], TestAt.Utc(At), keepBlockingDuringBreaks: false);

        Received.InOrder(() =>
        {
            _synchronizer.SyncAsync(TestAt.Utc(At), Arg.Any<CancellationToken>());
            _notifier.NotifyAsync(new BreakStarted(BreakKind.ShortBreak, KeepsBlocking: false), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RespondAsync_LongBreakStarted_ReportsLongBreak()
    {
        await CreateResponder().RespondAsync([Started(BreakKind.LongBreak)], TestAt.Utc(At), keepBlockingDuringBreaks: false);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(BreakKind.LongBreak, KeepsBlocking: false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_BreakStarted_KeepBlocking_ReportsItKeepsBlocking()
    {
        await CreateResponder().RespondAsync([Started()], TestAt.Utc(At), keepBlockingDuringBreaks: true);

        await _notifier.Received(1).NotifyAsync(
            new BreakStarted(BreakKind.ShortBreak, KeepsBlocking: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_BreakEnded_SyncsAndReportsBreakEnded()
    {
        await CreateResponder().RespondAsync([Ended()], TestAt.Utc(At), keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(TestAt.Utc(At), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_WholeBreakCrossedAtOnce_SyncsOnceAndOnlyAnnouncesWhereTheSessionIsNow()
    {
        // Laptop slept through the entire break: "Break time" would be stale.
        await CreateResponder().RespondAsync([Started(), Ended()], TestAt.Utc(At), keepBlockingDuringBreaks: false);

        await _synchronizer.Received(1).SyncAsync(TestAt.Utc(At), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(Arg.Any<NenNotification>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new BreakEnded(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespondAsync_NoTransitions_DoesNothing()
    {
        await CreateResponder().RespondAsync([], TestAt.Utc(At), keepBlockingDuringBreaks: false);

        await _synchronizer.DidNotReceiveWithAnyArgs().SyncAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!);
    }
}
