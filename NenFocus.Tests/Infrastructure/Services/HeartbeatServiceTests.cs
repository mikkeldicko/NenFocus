using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NenFocus.Tests.Infrastructure.Services;

/// <summary>
/// The one heartbeat (Gap 89 Slice A): one "now" per tick, every clock-driven step in a fixed order
/// (auto-end → missed blocks → contracts → enforcement → markers → Ritual prompt), each step failing
/// on its own, the first tick finished before the app's window opens, and an early tick the moment
/// the countdown reaches zero. The real composition root on a real SQLite file; only what lies
/// outside the app is faked — the clock, OS blocking, toasts and the countdown's timer.
/// </summary>
public class HeartbeatServiceTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private readonly ISystemClock _clock = TestClock.Create();
    private readonly IEnforcementService _enforcement = Substitute.For<IEnforcementService>();
    private readonly INotificationService _toaster = Substitute.For<INotificationService>();
    private readonly IActiveSessionMonitor _monitor = Substitute.For<IActiveSessionMonitor>();
    private readonly ServiceProvider _provider;

    public HeartbeatServiceTests()
    {
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetNow(Now);
        _monitor.Current.Returns(TimelineReading.None);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusHeartbeatTest_{Guid.NewGuid()}.db"));
        // Registered after AddInfrastructure, so these win.
        services.AddSingleton(_clock);
        services.AddSingleton(_enforcement);
        services.AddSingleton(_toaster);
        services.AddSingleton(_monitor);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NenFocusDbContext>().Database.Migrate();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private HeartbeatService Heartbeat => _provider.GetServices<IHostedService>().OfType<HeartbeatService>().Single();

    // ── Seeding ──────────────────────────────────────────────────────────────

    private async Task SeedAsync(Action<IServiceProvider> add)
    {
        using var scope = _provider.CreateScope();
        add(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
    }

    /// <summary>A 30-minute spontaneous session started <paramref name="startedMinutesAgo"/> minutes before Now.</summary>
    private async Task<Guid> SeedRunningSessionAsync(int startedMinutesAgo)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-startedMinutesAgo)));
        await SeedAsync(sp => sp.GetRequiredService<ISessionRepository>().Add(session));
        return session.Id;
    }

    private Task SeedMissedFocusBlockAsync() => SeedAsync(sp => sp.GetRequiredService<IBlockRepository>()
        .Add(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-2).AddMinutes(-30)))));

    private static NenContract Contract(DateOnly start, DateOnly end, DateOnly sealedOn) => TestContracts.Seal(
        id: Guid.NewGuid(), name: "Focused Week", goal: new NenContractGoal("Ship it"), strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(start, end, new DailyFloorRule.None()), floorRule: new DailyFloorRule.None(),
        allowCancellation: false, plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private Task SeedContractAsync(NenContract contract) =>
        SeedAsync(sp => sp.GetRequiredService<INenContractRepository>().Add(contract));

    private async Task<SessionStatus> StoredSessionStatusAsync(Guid sessionId)
    {
        using var scope = _provider.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISessionRepository>().GetByIdAsync(sessionId))!.Status;
    }

    // ── The tick ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneTick_RunsItsStepsInTheAgreedOrder()
    {
        // Everything happened while the app was closed: a session ran out, a block was missed, a
        // contract ended. One tick resolves all three, in the order the Shell shows them at startup.
        await SeedRunningSessionAsync(startedMinutesAgo: 120);
        await SeedMissedFocusBlockAsync();
        await SeedContractAsync(Contract(Today.AddDays(-10), Today.AddDays(-1), sealedOn: Today.AddDays(-10)));

        var happened = new List<string>();
        var recaps = _provider.GetRequiredService<IRecapRelay>();
        recaps.PostSessionsReady += (_, screens) => happened.AddRange(screens.Select(s => $"post-session {s.Status}"));
        recaps.PostContractsReady += (_, _) => happened.Add("contract recap");
        _enforcement.When(e => e.RemoveEnforcementAsync(Arg.Any<CancellationToken>())).Do(_ => happened.Add("sync"));
        _enforcement.When(e => e.ApplyPolicyAsync(Arg.Any<ActivePolicySlice>(), Arg.Any<CancellationToken>())).Do(_ => happened.Add("sync"));

        await Heartbeat.TickAsync(CancellationToken.None);

        var ended = happened.IndexOf("post-session Completed");
        var missed = happened.IndexOf("post-session Missed");
        var recap = happened.IndexOf("contract recap");
        ended.Should().BeGreaterThanOrEqualTo(0, string.Join(", ", happened));
        missed.Should().BeGreaterThan(ended, "missed blocks come after the auto-end");
        recap.Should().BeGreaterThan(missed, "contracts come after missed blocks, so a recap reads the tick's missed sessions");
        happened.LastIndexOf("sync").Should().BeGreaterThan(recap, "enforcement syncs after every state change in the tick");
    }

    [Fact]
    public async Task AStepThatThrows_DoesNotStopTheStepsAfterIt()
    {
        // A contract due to start toasts its activation; the toaster fails, so the contract step throws.
        // Enforcement must still get its turn in the same tick.
        await SeedContractAsync(Contract(Today, Today.AddDays(6), sealedOn: Today.AddDays(-1)));
        _toaster.ShowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The toaster is broken."));

        await Heartbeat.TickAsync(CancellationToken.None);

        await _enforcement.Received(1).RemoveEnforcementAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Starting_FinishesTheFirstTick_BeforeItReturns()
    {
        // Program.cs awaits the host's start before building the window, so the Shell's startup
        // queue sees what happened while the app was closed.
        var sessionId = await SeedRunningSessionAsync(startedMinutesAgo: 120);
        using var stop = new CancellationTokenSource();

        await Heartbeat.StartAsync(stop.Token);
        try
        {
            (await StoredSessionStatusAsync(sessionId)).Should().Be(SessionStatus.Completed);
        }
        finally
        {
            await Heartbeat.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenTheCountdownReachesZero_ItTicksAtOnce_NotAtTheNextBeat()
    {
        var sessionId = await SeedRunningSessionAsync(startedMinutesAgo: 10); // ends at Now + 20
        await Heartbeat.StartAsync(CancellationToken.None);
        try
        {
            _clock.SetNow(Now.AddMinutes(20));
            _monitor.Tick += Raise.Event<EventHandler<TimelineReading>>(_monitor,
                new TimelineReading(TimeSpan.Zero, 1.0, null, TimeSpan.Zero, 0, 0, false, HasEnded: true));

            // Well inside the 5-second beat: only an early tick can have ended it by then.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (await StoredSessionStatusAsync(sessionId) != SessionStatus.Completed && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            (await StoredSessionStatusAsync(sessionId)).Should().Be(SessionStatus.Completed);
        }
        finally
        {
            await Heartbeat.StopAsync(CancellationToken.None);
        }
    }
}
