using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class WindowsNotificationService : INotificationService, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingReminders = new();
    private readonly ILogger<WindowsNotificationService> _logger;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger)
    {
        _logger = logger;
    }

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
        catch (Exception ex)
        {
            // Toast notifications are best-effort: a COM/registration failure must not break
            // callers, but it must not vanish silently either.
            _logger.LogWarning(ex, "Toast notification failed: {Title}", title);
        }

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
        catch (Exception ex)
        {
            // Fire-and-forget task: without this, a toast failure would be an unobserved exception.
            _logger.LogWarning(ex, "Break reminder toast failed");
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