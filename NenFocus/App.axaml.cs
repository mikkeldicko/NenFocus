using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;
using NenFocus.Views;
using System.Linq;

namespace NenFocus;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            desktop.ShutdownRequested += async (_, _) =>
            {
                using (var scope = Program.AppHost!.Services.CreateScope())
                {
                    var enforcement = scope.ServiceProvider.GetRequiredService<IEnforcementService>();
                    var sessionState = scope.ServiceProvider.GetRequiredService<ISessionStateService>();
                    var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

                    if (sessionState.ActiveSession is { } sessionDto)
                    {
                        var session = await sessionRepository.GetByIdAsync(sessionDto.Id);
                        if (session != null)
                        {
                            // Use the new Sync method to update the entity safely
                            session.SyncState(sessionDto.BreaksTaken, sessionDto.BreakStartedAt);

                            await sessionRepository.SaveAsync(session);
                        }
                    }
                    else
                    {
                        await enforcement.RemoveEnforcementAsync();
                    }
                }
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = Program.AppHost!.Services.GetRequiredService<ShellViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Use the full explicit path to reach the static DataValidators collection
        var dataValidators = Avalonia.Data.Core.Plugins.BindingPlugins.DataValidators;

        var plugin = dataValidators.OfType<DataAnnotationsValidationPlugin>().FirstOrDefault();
        if (plugin != null)
        {
            dataValidators.Remove(plugin);
        }
    }
}