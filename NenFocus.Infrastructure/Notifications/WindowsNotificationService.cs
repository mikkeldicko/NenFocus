using System.Collections.Concurrent;
using Microsoft.Toolkit.Uwp.Notifications;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Notifications;

/// <summary>
/// Windows implementation of <see cref="INotificationService"/> using
/// Microsoft.Toolkit.Uwp.Notifications for system toast delivery.
///
/// Break reminders are scheduled in-process via Task.Delay + CancellationTokenSource.
/// Each reminder is tracked by a GUID token so individual reminders can be cancelled
/// independently (e.g. the user clicks "Skip" on a break).
/// </summary>
public sealed class WindowsNotificationService : INotificationService, IDisposable
{
    // Maps reminder token → its CTS so we can cancel individual reminders.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingReminders = new();

    // -------------------------------------------------------------------------
    // INotificationService
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task NotifyAsync(
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .Show();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reminder fires after <paramref name="interval"/> elapses. The toast
    /// message includes the break duration so the user knows how long the break is.
    /// The returned token must be passed to <see cref="CancelReminderAsync"/> if
    /// the session ends before the reminder fires.
    /// </remarks>
    public Task<string> ScheduleBreakReminderAsync(
        TimeSpan interval,
        TimeSpan breakDuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        _pendingReminders[token] = cts;

        // Fire-and-forget; RunReminderAsync handles its own cancellation and cleanup.
        _ = RunReminderAsync(token, interval, breakDuration, cts.Token);

        return Task.FromResult(token);
    }

    /// <inheritdoc/>
    public Task CancelReminderAsync(
        string reminderToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_pendingReminders.TryRemove(reminderToken, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelAllRemindersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot the keys first — TryRemove is safe on ConcurrentDictionary
        // but we want to avoid iterating while mutating.
        foreach (var key in _pendingReminders.Keys.ToList())
        {
            if (_pendingReminders.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task RunReminderAsync(
        string token,
        TimeSpan interval,
        TimeSpan breakDuration,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(interval, ct);

            var breakMinutes = (int)Math.Ceiling(breakDuration.TotalMinutes);
            var breakText = breakMinutes == 1 ? "1 minute" : $"{breakMinutes} minutes";

            new ToastContentBuilder()
                .AddText("Time for a break")
                .AddText($"Take {breakText} away from the screen. Your session will resume automatically.")
                .Show();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the session ends or the user skips.
            // Nothing to do here — finally block handles cleanup.
        }
        finally
        {
            // Remove and dispose the CTS if it hasn't already been claimed
            // by CancelReminderAsync or CancelAllRemindersAsync.
            if (_pendingReminders.TryRemove(token, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        // Cancel and dispose every pending reminder when the service is torn down
        // (e.g. app shutdown mid-session).
        foreach (var cts in _pendingReminders.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _pendingReminders.Clear();
    }
}