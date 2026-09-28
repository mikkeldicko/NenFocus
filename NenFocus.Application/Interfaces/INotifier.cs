using NenFocus.Application.Notifications;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The only way anything tells the user something. Sources report a fact; this owns all
/// wording (and, later, whether a kind of notification is muted). The only holder of
/// <see cref="INotificationService"/>.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(NenNotification fact, CancellationToken ct = default);
}
