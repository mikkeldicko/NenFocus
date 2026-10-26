using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Services;

/// <summary>
/// Every notification's wording, in one place. Pure: turns a fact into a title and body and
/// hands it to the toaster — no rules about when, no database.
/// </summary>
public sealed class Notifier : INotifier
{
    private readonly INotificationService _toaster;

    public Notifier(INotificationService toaster) => _toaster = toaster;

    public Task NotifyAsync(NenNotification fact, CancellationToken ct = default)
    {
        var (title, body) = Compose(fact);
        return _toaster.ShowAsync(title, body, ct);
    }

    private static (string Title, string Body) Compose(NenNotification fact) => fact switch
    {
        BlockStartingSoon f => (
            $"{Kind(f.Type)} block starting soon",
            $"{BlockName(f.Type, f.Name)} starts in {Minutes(f.MinutesLeft)}. " +
            (f.Type == BlockPresetType.Focus ? "Get ready for the Ritual." : "Blocking turns on then.")),

        BlockStarted f => (
            $"{Kind(f.Type)} block started",
            $"{BlockName(f.Type, f.Name)} has started. Blocking is on." +
            (f.Type == BlockPresetType.Focus ? " Complete the Ritual to begin your session." : "")),

        BlockEnded f => (
            $"{Kind(f.Type)} block ended",
            f.Type == BlockPresetType.Focus
                ? $"{BlockName(f.Type, f.Name)} ended without a session."
                : $"{BlockName(f.Type, f.Name)} has ended. Blocking is off."),

        SessionStarted { BookingType: FocusBlockBookingType.Planned } f => (
            "Nen session started",
            $"{BlockName(BlockPresetType.Focus, f.BlockName)} is on. Stay locked in."),

        SessionStarted => ("Nen session started", "Focus session is now active. Stay locked in."),

        SessionCompleted { BookingType: FocusBlockBookingType.Planned } f => (
            "Session complete",
            $"{BlockName(BlockPresetType.Focus, f.BlockName)} complete. Great work."),

        SessionCompleted => ("Session complete", "Great work. Your focus session has ended."),

        BreakStarted f => (
            f.Kind == BreakKind.LongBreak ? "Long break time" : "Break time",
            f.KeepsBlocking ? "Step away and recharge. Blocking stays on (contract rule)." : "Step away and recharge."),

        BreakEnded => ("Break over", "Back to focus."),

        ContractActivated f => ("Contract started", $"Your contract \"{f.Name}\" is now active."),

        ContractCompleted f => ("Contract complete", $"Your contract \"{f.Name}\" has ended."),

        _ => throw new ArgumentOutOfRangeException(nameof(fact), fact.GetType().Name, "No wording for this notification.")
    };

    private static string Kind(BlockPresetType type) => type == BlockPresetType.Focus ? "Focus" : "Background";

    private static string BlockName(BlockPresetType type, string? name) =>
        name is { Length: > 0 } ? $"\"{name}\"" : $"Your {Kind(type)} block";

    private static string Minutes(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";
}
