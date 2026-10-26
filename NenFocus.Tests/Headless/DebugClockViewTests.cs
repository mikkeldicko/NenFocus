using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.ViewModels.DevTools;
using NenFocus.ViewModels.Planner;
using NenFocus.Views.DevTools;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice A gave the screen one display clock with two paces. DisplayClockTests checks that
/// a debug jump raises both at once; this checks the screens listen: the debug overlay's time moves
/// on each second, and the Planner's now-line moves on the minute pace.
/// </summary>
public class DebugClockViewTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    [AvaloniaFact]
    public void TheOverlaysTime_MovesOnEachSecond()
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(Now);
        var display = Substitute.For<IDisplayClock>();
        var vm = new DebugClockViewModel(clock, Substitute.For<IDebugClockController>(), display);
        var view = new DebugClockView { DataContext = vm };
        Show(view);
        vm.ToggleVisibleCommand.Execute(null);
        Settle();
        VisibleTexts(view).Should().Contain("Oct 26, 09:00:00");

        clock.SetNow(Now.AddSeconds(1));
        display.SecondTicked += Raise.Event();
        Settle();
        VisibleTexts(view).Should().Contain("Oct 26, 09:00:01");

        clock.SetNow(Now.AddHours(1).AddSeconds(1)); // a +1h jump raises the second pace at once
        display.SecondTicked += Raise.Event();
        Settle();
        VisibleTexts(view).Should().Contain("Oct 26, 10:00:01");
    }

    [AvaloniaFact]
    public void ThePlannersNowLine_MovesOnTheMinutePace()
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(Now);
        var display = Substitute.For<IDisplayClock>();
        var vm = new PlannerPageViewModel(new FakeQueryDispatcher(), Substitute.For<ICommandDispatcher>(), display,
            Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), clock);
        Settle();
        var before = vm.CurrentTimeY;

        clock.SetNow(Now.AddHours(1)); // a +1h jump raises the minute pace at once
        display.MinuteTicked += Raise.Event();
        Settle();

        vm.CurrentTimeY.Should().BeApproximately(before + PlannerPageViewModel.HourRowHeight, 0.001);
    }
}
