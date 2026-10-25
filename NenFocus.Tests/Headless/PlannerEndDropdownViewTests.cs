using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The Planner's block form and its End dropdown — a plain TextBox + Popup the code-behind drives
/// through focus and key events, so it's the part of the Planner most exposed to input changes.
/// </summary>
public class PlannerEndDropdownViewTests
{
    [AvaloniaFact]
    public async Task FocusingEndOpensDurationLabelledOptionsAndArrowKeysStepThroughThem()
    {
        var vm = CreatePlanner();
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        Settle();
        vm.IsFormVisible.Should().BeTrue();
        vm.FormStartTime.Should().Be("13:00");
        vm.FormEndTime.Should().Be("14:00");

        var endBox = view.FindControl<TextBox>("FormEndTimeBox")!;
        endBox.Focus();
        Settle();

        var popup = view.FindControl<Popup>("FormEndTimePopup")!;
        popup.IsOpen.Should().BeTrue("focusing End opens its dropdown");
        VisibleTexts(popup.Child!).Should().ContainInOrder("13:15", "(15 mins)", "14:00", "(1 hr)", "14:30", "(1.5 hrs)");

        PressKey(endBox, Key.Down);
        vm.FormEndTime.Should().Be("14:30");
        PressKey(endBox, Key.Up);
        vm.FormEndTime.Should().Be("14:00");

        PressKey(endBox, Key.Escape);
        popup.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// Gap 104: the form already starts at "09:00", so a first block there changed nothing that
    /// used to rebuild the End list — it opened empty. Opening the form must build it every time.
    /// </summary>
    [AvaloniaFact]
    public async Task FirstBlockAtTheFormsDefaultStartStillGetsEndOptions()
    {
        var vm = CreatePlanner();
        Show(new PlannerPageView { DataContext = vm });

        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 9));

        vm.EndTimeOptions.Select(o => o.TimeText).Should().StartWith(["09:15", "09:30", "09:45", "10:00"]);
    }

    private static PlannerPageViewModel CreatePlanner()
    {
        var queries = new FakeQueryDispatcher().Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ =>
            [new() { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus }]);
        var clock = Substitute.For<IDisplayClock>();
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc)); // a Monday: column 1 (Tuesday) is still ahead
        return new PlannerPageViewModel(queries, Substitute.For<ICommandDispatcher>(), clock, Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), systemClock);
    }
}
