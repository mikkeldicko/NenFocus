namespace NenFocus.Application.Interfaces;

/// <summary>
/// Shows a system notification — nothing else; it knows no sessions or blocks. Only
/// <see cref="INotifier"/> calls it: everything else reports a fact to the notifier, which
/// owns the wording.
/// </summary>
public interface INotificationService
{
    Task ShowAsync(string title, string body, CancellationToken cancellationToken = default);
}
