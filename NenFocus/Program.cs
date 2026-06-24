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