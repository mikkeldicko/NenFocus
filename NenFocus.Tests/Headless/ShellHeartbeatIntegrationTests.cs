using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 92, A3, D′ end to end: the real composition root on a real SQLite file, the real heartbeat
/// tick (sweep → sync → markers → Ritual prompt, at one "now") and the real Shell. Only the OS
/// doors (enforcement, toasts) and the clock are fakes. Replaces the two D′ manual checks: a
/// planned session's Start leaves no Ritual behind, and a Ritual left open past its block's end
/// closes with the missed screen next — before the next block's Ritual when they are back to back.
/// </summary>
public class ShellHeartbeatIntegrationTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly ISystemClock _clock = TestClock.Create();
    private readonly ServiceProvider _provider;

    public ShellHeartbeatIntegrationTests()
    {
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetNow(Now);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusShellHeartbeatTest_{Guid.NewGuid()}.db"));
        // Registered after AddInfrastructure, so these win: no hosts file, no toasts, our clock.
        services.AddSingleton(_clock);
        services.AddSingleton(Substitute.For<IEnforcementService>());
        services.AddSingleton(Substitute.For<INotificationService>());
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NenFocusDbContext>().Database.Migrate();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private ScheduledEnforcementService Heartbeat =>
        _provider.GetServices<IHostedService>().OfType<ScheduledEnforcementService>().Single();

    private async Task TickAtAsync(DateTime at)
    {
        _clock.SetNow(at);
        await Heartbeat.TickAsync(CancellationToken.None);
        await SettleAsync();
    }

    // The Shell's handlers run on the UI thread and some await real database reads.
    private static async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            Settle();
            await Task.Delay(20);
        }
    }

    private async Task<Guid> SeedFocusPresetAsync()
    {
        var result = await _provider.GetRequiredService<ICommandDispatcher>().DispatchAsync(new CreateBlockPresetCommand
        {
            Name = "Deep Work",
            PresetType = BlockPresetType.Focus,
            WebsiteFilterType = WebsiteFilterType.Blocklist,
            Websites = ["example.com"]
        });
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.PresetId!.Value;
    }

    private async Task<Guid> SeedFocusBlockAsync(Guid presetId, DateTime start, DateTime end)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(start, end));
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPlannedBlockRepository>().AddAsync(block);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        return block.Id;
    }

    private async Task<ShellViewModel> CreateShellAsync()
    {
        var monitor = Substitute.For<IActiveSessionMonitor>();
        monitor.Current.Returns(TimelineReading.None);
        var shell = new ShellViewModel(
            _provider.GetRequiredService<IQueryDispatcher>(), _provider.GetRequiredService<ICommandDispatcher>(),
            _provider.GetRequiredService<IActiveSessionState>(), _provider.GetRequiredService<IRitualPrompt>(),
            _provider.GetRequiredService<IRecapRelay>(), monitor, Substitute.For<IPlannerClockService>(), _clock,
            Substitute.For<IDebugClockController>());
        await SettleAsync();
        return shell;
    }

    [AvaloniaFact]
    public async Task APlannedSessionsStart_LeavesNoRitualBehind()
    {
        var presetId = await SeedFocusPresetAsync();
        var blockId = await SeedFocusBlockAsync(presetId, Now.AddMinutes(-1), Now.AddMinutes(59));
        var shell = await CreateShellAsync();

        await TickAtAsync(Now);
        shell.IsRitualActive.Should().BeTrue("the block has started and awaits its Ritual");

        var start = await _provider.GetRequiredService<ICommandDispatcher>().DispatchAsync(new StartSessionCommand
        {
            Goal = "Thesis", PresetId = presetId, OriginatingBlockId = blockId, DurationMinutes = 59
        });
        start.Success.Should().BeTrue(start.ErrorMessage);
        await SettleAsync();

        _provider.GetRequiredService<IActiveSessionState>().ActiveSession!.OriginatingBlockId.Should().Be(blockId);

        await TickAtAsync(Now.AddSeconds(5));
        await TickAtAsync(Now.AddSeconds(10));

        shell.IsRitualActive.Should().BeFalse("the block no longer awaits its Ritual");
        shell.IsPostSessionActive.Should().BeFalse("nothing was missed");
    }

    [AvaloniaFact]
    public async Task ARitualLeftOpenPastItsEnd_ClosesAndTheMissedScreenFollows()
    {
        var presetId = await SeedFocusPresetAsync();
        await SeedFocusBlockAsync(presetId, Now.AddMinutes(-50), Now.AddMinutes(10));
        var shell = await CreateShellAsync();
        await TickAtAsync(Now);
        shell.IsRitualActive.Should().BeTrue();

        await TickAtAsync(Now.AddMinutes(10).AddSeconds(1));

        shell.IsRitualActive.Should().BeFalse();
        shell.IsPostSessionActive.Should().BeTrue();
        shell.PostSessionViewModel!.HeaderLabel.Should().Be("SESSION MISSED");
    }

    [AvaloniaFact]
    public async Task BackToBackBlocks_TheMissedScreenComesBeforeTheNextRitual()
    {
        var presetId = await SeedFocusPresetAsync();
        await SeedFocusBlockAsync(presetId, Now.AddMinutes(-50), Now.AddMinutes(10));
        await SeedFocusBlockAsync(presetId, Now.AddMinutes(10), Now.AddMinutes(70));
        var shell = await CreateShellAsync();
        await TickAtAsync(Now);

        await TickAtAsync(Now.AddMinutes(10).AddSeconds(1));

        shell.IsPostSessionActive.Should().BeTrue();
        shell.IsRitualActive.Should().BeFalse("the next Ritual waits behind the missed screen");

        await shell.PostSessionViewModel!.DoneCommand.ExecuteAsync(null);
        await SettleAsync();

        shell.IsPostSessionActive.Should().BeFalse();
        shell.IsRitualActive.Should().BeTrue();
    }
}
