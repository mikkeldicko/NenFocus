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
using NSubstitute;
using NenFocus.ViewModels.Main;
using NenFocus.ViewModels.Recaps;
using NenFocus.ViewModels.Shell;
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

    private HeartbeatService Heartbeat =>
        _provider.GetServices<IHostedService>().OfType<HeartbeatService>().Single();

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
        scope.ServiceProvider.GetRequiredService<IPlannedBlockRepository>().Add(block);
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
            _provider.GetRequiredService<IRecapRelay>(), monitor, Substitute.For<IDisplayClock>(), _clock,
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

    // ── Gap 89 Slice A: one path to the Post-Session screen, one heartbeat at startup ──

    /// <summary>Every Post-Session screen the Shell opens, in order — "shown once" is a count of 1.</summary>
    private static List<string> ScreensShown(ShellViewModel shell)
    {
        var shown = new List<string>();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.PostSessionViewModel) && shell.PostSessionViewModel is { } screen)
                shown.Add(screen.HeaderLabel);
            if (e.PropertyName == nameof(ShellViewModel.PostContractViewModel) && shell.PostContractViewModel is { } recap)
                shown.Add($"RECAP {recap.Name}");
        };
        return shown;
    }

    private async Task StartSpontaneousSessionAsync(Guid presetId, int minutes)
    {
        var start = await _provider.GetRequiredService<ICommandDispatcher>().DispatchAsync(new StartSessionCommand
        {
            Goal = "Thesis", PresetId = presetId, DurationMinutes = minutes
        });
        start.Success.Should().BeTrue(start.ErrorMessage);
        await SettleAsync();
    }

    /// <summary>What Program.Main does before the window opens: load the running session, then start the
    /// host, which runs the heartbeat's first tick (HeartbeatService.StartAsync).</summary>
    private async Task StartTheAppAsync()
    {
        await _provider.GetRequiredService<IActiveSessionState>().RefreshAsync();
        await Heartbeat.StartAsync(CancellationToken.None);
    }

    [AvaloniaFact]
    public async Task ASessionThatRunsOut_ShowsItsScreenOnce()
    {
        var presetId = await SeedFocusPresetAsync();
        var shell = await CreateShellAsync();
        var shown = ScreensShown(shell);
        await StartSpontaneousSessionAsync(presetId, minutes: 30);

        await TickAtAsync(Now.AddMinutes(30));

        shell.IsPostSessionActive.Should().BeTrue();
        shell.PostSessionViewModel!.HeaderLabel.Should().Be("SESSION COMPLETE");

        await shell.PostSessionViewModel.DoneCommand.ExecuteAsync(null);
        await SettleAsync();
        await TickAtAsync(Now.AddMinutes(30).AddSeconds(5));
        await TickAtAsync(Now.AddMinutes(30).AddSeconds(10));

        shown.Should().Equal("SESSION COMPLETE");
        shell.IsPostSessionActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task EndingEarly_ShowsItsScreenOnce()
    {
        var presetId = await SeedFocusPresetAsync();
        var shell = await CreateShellAsync();
        var shown = ScreensShown(shell);
        await StartSpontaneousSessionAsync(presetId, minutes: 30);
        var main = (MainPageViewModel)shell.NavItems[0].PageViewModel;

        _clock.SetNow(Now.AddMinutes(10));
        await main.EndSessionCommand.ExecuteAsync(null);
        await SettleAsync();

        shell.IsPostSessionActive.Should().BeTrue();

        await shell.PostSessionViewModel!.DoneCommand.ExecuteAsync(null);
        await SettleAsync();
        await TickAtAsync(Now.AddMinutes(10).AddSeconds(5));
        await TickAtAsync(Now.AddMinutes(30).AddSeconds(5)); // past its planned end: nothing left to auto-end

        shown.Should().Equal("SESSION COMPLETE");
        shell.IsPostSessionActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ClosedMidSession_ReopenedAfterItsEnd_ShowsItsScreenAtStartup()
    {
        var presetId = await SeedFocusPresetAsync();
        await StartSpontaneousSessionAsync(presetId, minutes: 30); // the app was open at 09:00 …

        _clock.SetNow(Now.AddHours(3)); // … closed, and reopened three hours later
        await StartTheAppAsync();
        try
        {
            var shell = await CreateShellAsync();

            shell.IsPostSessionActive.Should().BeTrue("the session's time ran out while the app was closed");
            shell.PostSessionViewModel!.HeaderLabel.Should().Be("SESSION COMPLETE");
            shell.PostSessionViewModel.EndedAt.Should().Be(Now.AddMinutes(30), "recorded at its planned end, not at reopening");
        }
        finally
        {
            await Heartbeat.StopAsync(CancellationToken.None);
        }
    }

    [AvaloniaFact]
    public async Task AContractThatEndedWhileClosed_ShowsItsRecapAtStartup_AfterTheMissedScreens()
    {
        var presetId = await SeedFocusPresetAsync();
        await SeedFocusBlockAsync(presetId, Now.AddHours(-3), Now.AddHours(-2)); // never started: missed
        var today = DateOnly.FromDateTime(Now);
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(), name: "Focused Week", goal: new NenContractGoal("Ship it"), strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(today.AddDays(-10), today.AddDays(-1), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(), allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(today.AddDays(-10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<INenContractRepository>().Add(contract);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await StartTheAppAsync();
        try
        {
            var shell = await CreateShellAsync();
            shell.IsPostSessionActive.Should().BeTrue();
            shell.PostSessionViewModel!.HeaderLabel.Should().Be("SESSION MISSED", "what you missed comes first");
            shell.IsPostContractActive.Should().BeFalse("the recap waits behind it");

            await shell.PostSessionViewModel.DoneCommand.ExecuteAsync(null);
            await SettleAsync();

            shell.IsPostContractActive.Should().BeTrue();
            shell.PostContractViewModel!.Name.Should().Be("Focused Week");
        }
        finally
        {
            await Heartbeat.StopAsync(CancellationToken.None);
        }
    }
}
