using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Enums;
using NenFocus.Views.Controls;
using NSubstitute;
using NenFocus.ViewModels.Planner;
using NenFocus.Views.Planner;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The Planner's block form and its Start/End fields — TimeOfDayBox controls bound to times
/// (Gap 89 Slice B), the part of the Planner most exposed to input changes.
/// </summary>
public class PlannerEndDropdownViewTests
{
    private static readonly TimeSpan T1300 = new(13, 0, 0), T1400 = new(14, 0, 0);

    [AvaloniaFact]
    public async Task FocusingEndOpensDurationLabelledOptionsAndArrowKeysStepThroughThem()
    {
        var vm = CreatePlanner();
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        Settle();
        vm.IsFormVisible.Should().BeTrue();
        vm.FormStartTime.Should().Be(T1300);
        vm.FormEndTime.Should().Be(T1400);

        var (endBox, popup) = Parts(view, "FormEndTimeField");
        endBox.Text.Should().Be("14:00");
        endBox.Focus();
        Settle();

        popup.IsOpen.Should().BeTrue("focusing End opens its dropdown");
        VisibleTexts(popup.Child!).Should().ContainInOrder("13:15", "(15 mins)", "14:00", "(1 hr)", "14:30", "(1.5 hrs)");

        PressKey(endBox, Key.Down);
        vm.FormEndTime.Should().Be(new TimeSpan(14, 30, 0));
        PressKey(endBox, Key.Up);
        vm.FormEndTime.Should().Be(T1400);

        PressKey(endBox, Key.Escape);
        popup.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// Gap 104: the form already starts at 09:00, so a first block there changed nothing that
    /// used to rebuild the End list — it opened empty. Opening the form must build it every time.
    /// </summary>
    [AvaloniaFact]
    public async Task FirstBlockAtTheFormsDefaultStartStillGetsEndOptions()
    {
        var vm = CreatePlanner();
        Show(new PlannerPageView { DataContext = vm });

        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 9));

        vm.EndTimeOptions.Should().StartWith([new TimeSpan(9, 15, 0), new TimeSpan(9, 30, 0), new TimeSpan(9, 45, 0), new TimeSpan(10, 0, 0)]);
    }

    [AvaloniaFact]
    public async Task TypingStart_LeavesEndAlone_UntilStartIsLeft_ThenEndKeepsTheLength()
    {
        var vm = CreatePlanner();
        var view = new PlannerPageView { DataContext = vm };
        var window = Show(view);
        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        Settle();

        var (startBox, _) = Parts(view, "FormStartTimeField");
        Type(window, startBox, "10:3");
        vm.FormEndTime.Should().Be(T1400, "End doesn't jump to 11:03 for a half-typed 10:03");
        window.KeyTextInput("0");
        Settle();
        vm.FormEndTime.Should().Be(T1400);

        Parts(view, "FormEndTimeField").Box.Focus();
        Settle();
        vm.FormStartTime.Should().Be(new TimeSpan(10, 30, 0));
        vm.FormEndTime.Should().Be(new TimeSpan(11, 30, 0), "the block keeps its one hour");
    }

    [AvaloniaFact]
    public async Task SavingStraightAfterTyping_SavesWhatWasTyped()
    {
        var commands = Substitute.For<ICommandDispatcher>();
        CreatePlannedBlockCommand? sent = null;
        commands.DispatchAsync(Arg.Do<CreatePlannedBlockCommand>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(CreatePlannedBlockResult.Ok(Guid.NewGuid()));
        var vm = CreatePlanner(commands);
        var view = new PlannerPageView { DataContext = vm };
        var window = Show(view);
        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        Settle();

        Type(window, Parts(view, "FormStartTimeField").Box, "10:30");
        Click(window, ButtonWithText(view, "Create Block"));

        sent.Should().NotBeNull("clicking Save leaves the field first, so the typed time is committed");
        sent!.Slot.Should().Be(At.Slot(new DateTime(2026, 9, 29, 10, 30, 0), new DateTime(2026, 9, 29, 11, 30, 0)));
    }

    private static (TextBox Box, Popup DropDown) Parts(PlannerPageView view, string fieldName)
    {
        var field = view.FindControl<TimeOfDayBox>(fieldName)!;
        return (field.GetVisualDescendants().OfType<TextBox>().Single(), field.GetLogicalDescendants().OfType<Popup>().Single());
    }

    private static PlannerPageViewModel CreatePlanner(ICommandDispatcher? commands = null)
    {
        var queries = new FakeQueryDispatcher().Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ =>
            [new() { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus }]);
        var clock = Substitute.For<IDisplayClock>();
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc)); // a Monday: column 1 (Tuesday) is still ahead
        return new PlannerPageViewModel(queries, commands ?? Substitute.For<ICommandDispatcher>(), clock, Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), systemClock);
    }
}
