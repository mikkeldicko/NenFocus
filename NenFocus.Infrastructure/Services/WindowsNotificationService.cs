using System.Collections.Concurrent;
using Microsoft.Toolkit.Uwp.Notifications;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class WindowsNotificationService : INotificationService, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingReminders = new();

    public Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show(toast => { toast.ExpirationTime = DateTime.Now.AddDays(1); });
        }
        catch { /* Toast notifications are best-effort; COM/registration failures must not break callers. */ }

        return Task.CompletedTask;
    }

    public Task<string> ScheduleBreakReminderAsync(
        TimeSpan interval,
        TimeSpan breakDuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();
        _pendingReminders[token] = cts;

        _ = RunReminderAsync(token, interval, breakDuration, cts.Token);

        return Task.FromResult(token);
    }

    public Task CancelReminderAsync(string reminderToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pendingReminders.TryRemove(reminderToken, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        return Task.CompletedTask;
    }

    public Task CancelAllRemindersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            // Normal behavior on skip/stop
        }
        finally
        {
            if (_pendingReminders.TryRemove(token, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var cts in _pendingReminders.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _pendingReminders.Clear();
    }
}