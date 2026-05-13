namespace NenFocus.Application.Interfaces;

/// <summary>
/// Contract for sending system notifications and scheduling timed alerts.
/// Implemented by NenFocus.Infrastructure using platform notification APIs.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends an immediate system notification.
    /// Used for Background Block start, session end, and break prompts.
    /// </summary>
    Task NotifyAsync(
        string title,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a break reminder notification to fire after the given interval.
    /// Returns a reminder token that can be used to cancel the reminder.
    /// </summary>
    Task<string> ScheduleBreakReminderAsync(
        TimeSpan interval,
        TimeSpan breakDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a previously scheduled break reminder by its token.
    /// Called when the session ends before the reminder fires.
    /// </summary>
    Task CancelReminderAsync(string reminderToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all pending scheduled reminders.
    /// Called on session end to clean up any remaining break reminders.
    /// </summary>
    Task CancelAllRemindersAsync(CancellationToken cancellationToken = default);
}
