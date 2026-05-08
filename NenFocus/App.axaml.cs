using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
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
                var enforcement = Program.AppHost!.Services.GetRequiredService<IEnforcementService>();
                var sessionState = Program.AppHost!.Services.GetRequiredService<ISessionStateService>();

                if (sessionState.ActiveSession is { } session)
                {
                    // Save session end time so it can be restored on next launch
                    var endTime = session.StartedAt.AddMinutes(session.DurationMinutes);
                    Program.Persistence?.Save(session, endTime);
                }
                else
                {
                    // No active session — clean up enforcement and clear any stale file
                    Program.Persistence?.Clear();
                    await enforcement.RemoveEnforcementAsync();
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