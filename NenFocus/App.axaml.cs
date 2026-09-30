using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NenFocus.Application.Interfaces;
using NenFocus.ViewModels;
using NenFocus.Views;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

            // Closing leaves blocking exactly as PolicyEngine says it should be right now — a
            // running session, an active Focus or Background block — through the one door that
            // applies policy (Gap 101). The first request is held until the sync finishes, so a
            // hosts-file write is never cut off by the process exiting; the second passes through.
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (_enforcementSyncedForShutdown) return;

                e.Cancel = true;
                await SyncEnforcementForShutdownAsync();
                _enforcementSyncedForShutdown = true;
                desktop.Shutdown();
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = Program.AppHost!.Services.GetRequiredService<ShellViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static readonly TimeSpan ShutdownSyncLimit = TimeSpan.FromSeconds(2);

    private bool _enforcementSyncedForShutdown;

    // Fail closed: if the sync fails or runs past the limit (well inside Windows' own shutdown
    // patience), blocking is left exactly as it is.
    private static async Task SyncEnforcementForShutdownAsync()
    {
        var services = Program.AppHost!.Services;
        try
        {
            using var limit = new CancellationTokenSource(ShutdownSyncLimit);
            var clock = services.GetRequiredService<ISystemClock>();
            await services.GetRequiredService<IEnforcementSynchronizer>().SyncAsync(clock.Now, limit.Token);
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Enforcement sync at shutdown failed; blocking left as it was");
        }
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