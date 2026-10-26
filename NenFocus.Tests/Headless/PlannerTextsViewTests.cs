using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.Application.Queries.GetBlocksForDay;
using NenFocus.Application.UseCases.Planner.CreateBlock;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Planner;
using NenFocus.Views.Controls;
using NenFocus.Views.Planner;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice B moved the Planner's display text out of the ViewModel into converters and its
/// time fields into TimeOfDayBox. These render the real Planner on a week of data and read the
/// screen: the Split dialog's four fields, the contract banner, the day badges and Bonus pill,
/// the hour column, End past midnight, and a refused save's red line.
/// </summary>
public class PlannerTextsViewTests
{
    // Monday 2026-09-28 … Sunday 2026-10-04; "now" is Tuesday 07:00 UTC, zone UTC.
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateTime Now = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);

    // ── Split ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Split_FourFieldsPrefill_EndRowsShowLengths_AndANewStartKeepsThePiecesLength()
    {
        // A bound block that may be split: 10:00–11:00 on Tuesday.
        var block = new BlockDto
        {
            Id = Guid.NewGuid(), PresetId = Guid.NewGuid(), PresetName = "Deep Work", BlockPresetType = BlockPresetType.Focus,
            Shown = At.Slot(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 11, 0, 0, DateTimeKind.Utc)),
            Allowances = new BlockChangeAllowances(CanReschedule: true, CanChangePreset: false, CanSplit: true, CanDelete: false),
        };
        var queries = new FakeQueryDispatcher()
            .Answer<GetBlocksForDayQuery, IReadOnlyList<BlockDto>>(q => q.Date == Tuesday ? [block] : []);
        var vm = Planner(queries);
        var view = new PlannerPageView { DataContext = vm };
        var window = Show(view);

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.OpenSplitDialogCommand.Execute(null);
        Settle();

        Field(view, "SplitFirstStartTimeField").Box.Text.Should().Be("10:00");
        Field(view, "SplitFirstEndTimeField").Box.Text.Should().Be("10:30");
        Field(view, "SplitSecondStartTimeField").Box.Text.Should().Be("10:30");
        Field(view, "SplitSecondEndTimeField").Box.Text.Should().Be("11:00");

        var (secondEnd, secondEndList) = Field(view, "SplitSecondEndTimeField");
        secondEnd.Focus();
        Settle();
        secondEndList.IsOpen.Should().BeTrue();
        VisibleTexts(secondEndList.Child!).Should().ContainInOrder("10:45", "(15 mins)", "11:00", "(30 mins)", "11:30", "(1 hr)");
        PressKey(secondEnd, Avalonia.Input.Key.Escape);

        // Move the first piece's Start: its End follows, keeping 30 minutes.
        var (firstStart, _) = Field(view, "SplitFirstStartTimeField");
        Type(window, firstStart, "09:15");
        Field(view, "SplitFirstEndTimeField").Box.Focus();
        Settle();

        vm.SplitFirstStartTime.Should().Be(new TimeSpan(9, 15, 0));
        vm.SplitFirstEndTime.Should().Be(new TimeSpan(9, 45, 0), "the piece keeps its 30 minutes");
        Field(view, "SplitFirstEndTimeField").Box.Text.Should().Be("09:45");
    }

    // ── Contract week texts ─────────────────────────────────────────────────

    [AvaloniaFact]
    public void AContractWeek_ReadsAsBefore()
    {
        var contract = new NenContractRangeViewDto
        {
            Id = Guid.NewGuid(), Name = "Focused Week", StartDate = Monday, EndDate = Monday.AddDays(6),
            StrictnessRule = StrictnessRule.DailyFlexibility, Status = NenContractStatus.Active,
            DayNumber = 2, TotalDays = 7,
            TotalCompletedHours = 3.5, TotalCommittedHours = 10,
            Deficit = WholeMinutes.Of(90), TotalBonusHours = 0.75,
            DayStatuses =
            [
                new() { Date = Monday, IsRestDay = true, Timing = DayTiming.Past, IsMet = true },
                new() { Date = Tuesday, CurrentHours = 2, TargetHours = 3, Timing = DayTiming.Today, BonusHours = 1 },
                new() { Date = Monday.AddDays(2), CurrentHours = 3, TargetHours = 3, Timing = DayTiming.Future, IsMet = true },
            ],
        };
        var queries = new FakeQueryDispatcher()
            .Answer<GetNenContractsForDateRangeQuery, IReadOnlyList<NenContractRangeViewDto>>(_ => [contract]);
        var view = new PlannerPageView { DataContext = Planner(queries) };
        Show(view);

        var texts = VisibleTexts(view);
        texts.Should().Contain("Contract: Focused Week — Day 2/7 — 3.5h / 10h completed — 1h 30m deficit — 45 min bonus");
        texts.Should().ContainInOrder("Rest", "2h / 3h", "+1h bonus", "3h");
        texts.Should().NotContain("3h / 3h", "a sealed contract's future day shows one number");
    }

    [AvaloniaFact]
    public void TheHourColumn_RunsFrom0000To2300()
    {
        var view = new PlannerPageView { DataContext = Planner(new FakeQueryDispatcher()) };
        Show(view);

        var hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
        VisibleTexts(view).Should().ContainInOrder(hours).And.NotContain("24:00");
    }

    [AvaloniaFact]
    public async Task AnEndPastMidnight_ShowsTheNextDaysDate()
    {
        var vm = Planner(WithPreset(new FakeQueryDispatcher()));
        var view = new PlannerPageView { DataContext = vm };
        var window = Show(view);
        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 23)); // Tuesday 23:00
        Settle();
        VisibleTexts(view).Should().NotContain("Sep 30, 2026");

        Type(window, Field(view, "FormEndTimeField").Box, "01:00");
        Field(view, "FormStartTimeField").Box.Focus();
        Settle();

        vm.FormEndTime.Should().Be(new TimeSpan(1, 0, 0));
        VisibleTexts(view).Should().Contain("Sep 30, 2026");
    }

    // ── Errors ───────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ARefusedSave_ShowsItsRedLineUnderTheForm()
    {
        const string refusal = "This time overlaps another Focus block.";
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<CreateBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(CreateBlockResult.Fail(refusal));
        var vm = Planner(WithPreset(new FakeQueryDispatcher()), commands);
        var view = new PlannerPageView { DataContext = vm };
        var window = Show(view);
        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        Settle();

        var save = ButtonWithText(view, "Create Block");
        Click(window, save);

        vm.IsFormVisible.Should().BeTrue("a refused save keeps the form open");
        var line = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == refusal);
        IsShown(line).Should().BeTrue();
        line.Classes.Should().Contain("error-text");
        Avalonia.VisualExtensions.TranslatePoint(line, new Avalonia.Point(0, 0), save)!.Value.Y.Should().BeLessThan(0, "the line sits above the form's buttons, under its fields");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (TextBox Box, Popup DropDown) Field(PlannerPageView view, string name)
    {
        var field = view.FindControl<TimeOfDayBox>(name)!;
        return (field.GetVisualDescendants().OfType<TextBox>().Single(), field.GetLogicalDescendants().OfType<Popup>().Single());
    }

    private static FakeQueryDispatcher WithPreset(FakeQueryDispatcher queries) =>
        queries.Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ =>
            [new() { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus }]);

    private static PlannerPageViewModel Planner(FakeQueryDispatcher queries, ICommandDispatcher? commands = null)
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(Now);
        return new PlannerPageViewModel(queries, commands ?? Substitute.For<ICommandDispatcher>(), Substitute.For<IDisplayClock>(),
            Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), clock);
    }
}
