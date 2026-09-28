using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;

namespace NenFocus.Tests.Infrastructure;

/// <summary>
/// INotifier is the one door to the toaster: it owns all wording. Anything else taking
/// INotificationService directly would scatter wording again, one "quick toast" at a time.
/// </summary>
public class NotificationDoorTests
{
    [Fact]
    public void OnlyTheNotifierTakesTheToaster()
    {
        var assemblies = new[]
        {
            typeof(Notifier).Assembly,                      // Application
            typeof(WindowsNotificationService).Assembly,    // Infrastructure
            typeof(ShellViewModel).Assembly                 // UI
        };

        var holders = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors().Any(c =>
                c.GetParameters().Any(p => p.ParameterType == typeof(INotificationService))))
            .Select(t => t.FullName);

        holders.Should().Equal(typeof(Notifier).FullName);
    }
}
