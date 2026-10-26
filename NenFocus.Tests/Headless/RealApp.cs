using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NenFocus.ViewModels.Shell;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The real composition root on a fresh SQLite file, as ShellHeartbeatIntegrationTests builds it:
/// only the OS doors (enforcement, toasts) and the clock are fakes. For screen tests that need
/// every query and command to be the real one.
/// </summary>
internal sealed class RealApp : IAsyncDisposable
{
    public ISystemClock Clock { get; } = TestClock.Create();
    public ServiceProvider Provider { get; }

    public RealApp(DateTime now)
    {
        Clock.SetTimeZoneOffset(TimeSpan.Zero);
        Clock.SetNow(now);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusRealAppTest_{Guid.NewGuid()}.db"));
        // Registered after AddInfrastructure, so these win: no hosts file, no toasts, our clock.
        services.AddSingleton(Clock);
        services.AddSingleton(Substitute.For<IEnforcementService>());
        services.AddSingleton(Substitute.For<INotificationService>());
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NenFocusDbContext>().Database.Migrate();
    }

    public IQueryDispatcher Queries => Provider.GetRequiredService<IQueryDispatcher>();
    public ICommandDispatcher Commands => Provider.GetRequiredService<ICommandDispatcher>();

    public ValueTask DisposeAsync() => Provider.DisposeAsync();

    /// <summary>Runs UI work and lets real database reads finish — the screens await them.</summary>
    public static async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            Settle();
            await Task.Delay(20);
        }
    }

    public async Task<Guid> SeedPresetAsync(string name, BlockPresetType type)
    {
        var result = await Commands.DispatchAsync(new CreateBlockPresetCommand
        {
            Name = name,
            PresetType = type,
            WebsiteFilterType = WebsiteFilterType.Blocklist,
            Websites = ["example.com"]
        });
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.PresetId!.Value;
    }

    /// <summary>The Shell as App.axaml.cs builds it, with the real session state, prompt and relays.</summary>
    public async Task<ShellViewModel> CreateShellAsync()
    {
        var shell = new ShellViewModel(
            Queries, Commands,
            Provider.GetRequiredService<IActiveSessionState>(), Provider.GetRequiredService<IRitualPrompt>(),
            Provider.GetRequiredService<IRecapRelay>(), Provider.GetRequiredService<IActiveSessionMonitor>(),
            Substitute.For<IDisplayClock>(), Clock, Substitute.For<IDebugClockController>());
        await SettleAsync();
        return shell;
    }
}
