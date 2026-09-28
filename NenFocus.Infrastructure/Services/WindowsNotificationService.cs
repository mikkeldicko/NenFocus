using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class WindowsNotificationService : INotificationService
{
    private readonly ILogger<WindowsNotificationService> _logger;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger)
    {
        _logger = logger;
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
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
}
