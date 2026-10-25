using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Services;
using NSubstitute;
using NenFocus.ViewModels.Main;
using NenFocus.Views.Main;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 92, A4: the Main page flips into and out of a break at the boundary — the real
/// ActiveSessionMonitor (its own 1-second timer) reading the session's SessionTimeline, through
/// MainPageViewModel, onto the rendered view. Only the clock is a fake, moved across the boundary.
/// </summary>
public class MainPageBreakFlipTests
{
    private static readonly DateTime Start = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    [AvaloniaFact]
    public async Task TheMainPage_SwitchesToTheBreakCountdownAtTheBoundaryAndBack()
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(Start.AddMinutes(24).AddSeconds(50));

        // Classic over one hour: work 0–25, break 25–30, work 30–60.
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Thesis"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Start), breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromHours(1)),
            breakPresetName: "Classic");
        var sessionState = Substitute.For<IActiveSessionState>();
        sessionState.ActiveSession.Returns(new SessionDto
        {
            Id = session.Id, Goal = "Thesis", PresetName = "Deep Work", Status = SessionStatus.Active,
            Timeline = session.Timeline, BreakPresetName = "Classic"
        });

        using var monitor = new ActiveSessionMonitor(sessionState, clock,
            Substitute.For<IBreakTransitionResponder>(), NullLogger<ActiveSessionMonitor>.Instance);
        var vm = new MainPageViewModel(() => { }, sessionState, monitor,
            Substitute.For<ICommandDispatcher>(), clock);
        var view = new MainPageView { DataContext = vm };
        Show(view);

        VisibleTexts(view).Should().NotContain("ON BREAK");
        view.FindControl<Grid>("BreakMarkersGrid")!.Children.Should().ContainSingle("one break, one mark");

        clock.SetNow(Start.AddMinutes(25).AddSeconds(1));
        await NextTickAsync(monitor);
        VisibleTexts(view).Should().Contain("ON BREAK");
        vm.BreakTimeRemaining.Should().Be(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        clock.SetNow(Start.AddMinutes(30).AddSeconds(1));
        await NextTickAsync(monitor);
        VisibleTexts(view).Should().NotContain("ON BREAK");
        vm.IsOnBreak.Should().BeFalse();
    }

    // Waits for the monitor's own timer to fire once more, then lets the UI catch up.
    private static async Task NextTickAsync(IActiveSessionMonitor monitor)
    {
        var ticked = new TaskCompletionSource();
        void OnTick(object? sender, TimelineReading reading) => ticked.TrySetResult();
        monitor.Tick += OnTick;
        try
        {
            (await Task.WhenAny(ticked.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(ticked.Task, "the monitor ticks every second");
        }
        finally
        {
            monitor.Tick -= OnTick;
        }

        for (var i = 0; i < 5; i++)
        {
            Settle();
            await Task.Delay(20);
        }
    }
}
