using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace NenFocus;

internal sealed class Program
{
    public static IHost? AppHost { get; private set; }

    private static string GetDbPath()
    {
        var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NenFocus");
        Directory.CreateDirectory(dbFolder);
        return Path.Combine(dbFolder, "NenFocus.db");
    }

    [STAThread]
    public static async Task Main(string[] args)
    {
        AppHost = CreateHostBuilder(args).Build();
        AppHost.EnsureDatabase();

        // Must complete before AppHost.StartAsync() (which runs the heartbeat's first tick)
        // and before the Avalonia app builds
        // below (which constructs ShellViewModel/MainPageViewModel) — otherwise both can
        // observe IActiveSessionState.ActiveSession as null even when a session is
        // genuinely active in the DB, momentarily triggering the Ritual overlay on
        // startup before rehydration catches up a few hundred milliseconds later.
        await AppHost.Services.GetRequiredService<IActiveSessionState>().RefreshAsync();

        // Awaits the heartbeat's first tick (HeartbeatService.StartAsync, Gap 89 Slice A): whatever
        // happened while the app was closed — a session whose time ran out, missed blocks, a contract
        // that ended — is saved before the window opens, so the Shell's startup queue finds it.
        await AppHost.StartAsync();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddInfrastructure(GetDbPath());
                services.AddTransient<ShellViewModel>();
            });

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}