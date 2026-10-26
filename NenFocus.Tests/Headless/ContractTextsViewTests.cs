using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Planner.ContractWizard;
using NenFocus.ViewModels.Presets;
using NenFocus.ViewModels.Recaps;
using NenFocus.Views.Planner.ContractWizard;
using NenFocus.Views.Presets;
using NenFocus.Views.Recaps;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice B moved the contract screens' dates, floor and Work Hours into converters
/// (DateRangeToLabelConverter, DailyFloorToLabelConverter, WorkHoursRangeToLabelConverter,
/// DateListToLabelConverter). These render the real views and read the words: the wizard's rules
/// and review steps, a contract preset card, and a Post-Contract recap.
/// </summary>
public class ContractTextsViewTests
{
    private static readonly DateOnly Start = new(2026, 10, 26);

    private static readonly ContractPresetDto ExamWeek = new()
    {
        Id = Guid.NewGuid(), Name = "Exam Week", StrictnessRule = StrictnessRule.DailyFlexibility,
        DailyFloorHours = 2, WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(17, 0),
        AllowCancellation = true,
    };

    [AvaloniaFact]
    public void TheWizardsRulesAndReviewSteps_ShowTheDatesFloorWorkHoursAndRestDays()
    {
        var queries = new FakeQueryDispatcher()
            .Answer<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>(_ => [ExamWeek]);
        var wizard = new ContractWizardViewModel(queries, Start, onReadyToSchedule: _ => { }, onCancel: () => { });
        var view = new ContractWizardView { DataContext = wizard };
        Show(view);

        var basics = (ContractBasicsStepViewModel)wizard.CurrentStep;
        basics.Name = "Focused Week";
        basics.Goal = "Ship it";
        Settle();
        basics.ContinueCommand.Execute(null);
        ((ContractModeStepViewModel)wizard.CurrentStep).ChooseAdvancedCommand.Execute(null);
        Settle();

        var rules = (ContractRulesStepViewModel)wizard.CurrentStep;
        rules.SelectedPreset = rules.SavedPresets.Single();
        rules.RestDayChips.Single(c => c.Date == Start.AddDays(2)).IsRestDay = true;
        Settle();
        VisibleTexts(view).Should().Contain(["Daily Flex", "2h / day", "09:00 – 17:00"]);

        rules.ContinueCommand.Execute(null);
        Settle();

        wizard.CurrentStep.Should().BeOfType<ContractReviewStepViewModel>();
        VisibleTexts(view).Should().ContainInOrder(
            "Focused Week",
            "Oct 26, 2026 – Nov 1, 2026",
            "Daily Flex",
            "2h / day",
            "09:00 – 17:00",
            "Oct 28");
    }

    [AvaloniaFact]
    public void AContractPresetCard_ShowsItsFloorAndWorkHours()
    {
        var noHours = new ContractPresetDto { Id = Guid.NewGuid(), Name = "Open Week", StrictnessRule = StrictnessRule.PeriodTimeBudget };
        var queries = new FakeQueryDispatcher()
            .Answer<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>(_ => [ExamWeek, noHours]);
        var view = new ContractPresetPageView { DataContext = new ContractPresetPageViewModel(queries, Substitute.For<ICommandDispatcher>()) };
        Show(view);

        VisibleTexts(view).Should().ContainInOrder(
            "Exam Week", "2h / day", "09:00 – 17:00",
            "Open Week", "Derived from scheduled blocks", "No constraint");
    }

    [AvaloniaFact]
    public void APostContractRecap_ShowsItsDateRangeAndDayTiles()
    {
        var recap = new NenContractRecapDto
        {
            ContractId = Guid.NewGuid(), Name = "Focused Week", Status = NenContractStatus.Completed,
            StartDate = new DateOnly(2026, 3, 5), EndDate = new DateOnly(2026, 3, 7),
            CommittedFocusHours = 6, TotalCompletedHours = 4.5,
            DailyBreakdown =
            [
                new() { Date = new DateOnly(2026, 3, 5), Percentage = 1.0 },
                new() { Date = new DateOnly(2026, 3, 6), IsRestDay = true },
                new() { Date = new DateOnly(2026, 3, 7), Percentage = 0.5 },
            ],
        };
        var view = new PostContractView { DataContext = new PostContractViewModel(recap, Substitute.For<ICommandDispatcher>(), () => { }) };
        Show(view);

        VisibleTexts(view).Should().ContainInOrder(
            "Mar 5, 2026 – Mar 7, 2026",
            "Mar 5", "100%", "Mar 6", "Rest", "Mar 7", "50%");
    }
}
