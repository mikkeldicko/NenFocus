using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;
using System;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;


namespace NenFocus;

internal sealed class Program
{
    public static IHost? AppHost { get; private set; }
    public static SessionPersistenceService? Persistence { get; private set; }

    [STAThread]
    public static async Task Main(string[] args)
    {
        // 1. Logic only needed at RUNTIME
        var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NenFocus");
        Directory.CreateDirectory(dbFolder);
        Persistence = new SessionPersistenceService(dbFolder);

        // 2. Use the helper to build the host
        AppHost = CreateHostBuilder(args).Build();

        await AppHost.StartAsync();
        AppHost.EnsureDatabase();

        // ... rest of your startup logic (Enforcement, etc)

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // EF Core tools look for this method specifically!
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        // We need a path for design-time. If EF is running, we can use a temp path.
        var designTimePath = Path.Combine(AppContext.BaseDirectory, "nenfocus.db");

        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddInfrastructure(designTimePath);
                services.AddTransient<ShellViewModel>();
            });
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}