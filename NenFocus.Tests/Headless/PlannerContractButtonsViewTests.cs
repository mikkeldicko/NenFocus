using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractCancellable;
using NenFocus.Application.Queries.GetInEffectNenContracts;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 92, D: the Nen Contract popover shows each contract's actions from what the Domain
/// answers (readiness, cancellable — tested on NenContract), never from the contract's status
/// itself: Edit and Delete on a Scheduled contract, Complete and Cancel on the Active one only
/// when allowed, nothing for a contract that is no longer in effect.
/// </summary>
public class PlannerContractButtonsViewTests
{
    private static readonly NenContractSummaryDto Active = new()
    {
        Id = Guid.NewGuid(), Name = "Exam Month", Status = NenContractStatus.Active,
        StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), StrictnessRule = StrictnessRule.DailyFlexibility
    };

    private static readonly NenContractSummaryDto Scheduled = new()
    {
        Id = Guid.NewGuid(), Name = "Thesis Sprint", Status = NenContractStatus.Scheduled,
        StartDate = new DateOnly(2026, 11, 2), EndDate = new DateOnly(2026, 11, 15), StrictnessRule = StrictnessRule.Locked
    };

    private static readonly ContractActionVerdictDto Allowed = new() { Outcome = ContractActionOutcome.Allowed };
    private static readonly ContractActionVerdictDto Refused = new() { Outcome = ContractActionOutcome.CancellationNotAllowed, Message = "No." };

    private static PlannerPageView OpenPopover(IReadOnlyList<NenContractSummaryDto> inEffect, bool ready, ContractActionVerdictDto cancel)
    {
        var queries = new FakeQueryDispatcher()
            .Answer<GetInEffectNenContractsQuery, IReadOnlyList<NenContractSummaryDto>>(_ => inEffect)
            .Answer<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto>(_ => new NenContractCompletionReadinessDto { IsReady = ready })
            .Answer<CheckNenContractCancellableQuery, ContractActionVerdictDto?>(_ => cancel);
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc));
        var vm = new PlannerPageViewModel(queries, Substitute.For<ICommandDispatcher>(), Substitute.For<IPlannerClockService>(),
            Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), systemClock);
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        vm.ToggleNenContractPopoverCommand.Execute(null);
        Settle();
        return view;
    }

    private static List<string> ShownActions(PlannerPageView view) =>
        view.GetLogicalDescendants().OfType<Button>()
            .Where(b => IsShown(b) && b.Content is "Complete" or "Cancel" or "Edit" or "Delete")
            .Select(b => (string)b.Content!)
            .ToList();

    [AvaloniaFact]
    public void AScheduledContract_OffersEditAndDelete_TheActiveOneCompleteAndCancelWhenAllowed()
    {
        var view = OpenPopover([Active, Scheduled], ready: true, Allowed);

        ShownActions(view).Should().BeEquivalentTo(["Complete", "Cancel", "Edit", "Delete"]);
    }

    [AvaloniaFact]
    public void TheActiveContract_HidesCompleteAndCancelWhenTheDomainRefusesThem()
    {
        // Something still owed, sealed without cancellation, or a session running.
        var view = OpenPopover([Active], ready: false, Refused);

        ShownActions(view).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void AContractNoLongerInEffect_OffersNothing()
    {
        // A Completed or Broken contract isn't in effect, so the popover doesn't list it.
        var view = OpenPopover([], ready: true, Allowed);

        ShownActions(view).Should().BeEmpty();
    }
}
