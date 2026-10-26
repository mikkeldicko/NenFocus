using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractCancellable;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetInEffectNenContracts;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Planner;
using NenFocus.ViewModels.Planner.ContractWizard;
using NenFocus.ViewModels.Presets;
using NenFocus.Views.Controls;
using NenFocus.Views.Planner;
using NenFocus.Views.Presets;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice B made every "Are you sure?" one ModalDialog. ModalDialogTests checks the control
/// on its own; this opens each of the seven real ones through its screen's commands and reads
/// what the user sees: the screen's own title and wording, Cancel/Keep before the red button —
/// and, for the two preset pages, a refusal shown inside the card.
/// </summary>
public class ConfirmDialogViewTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);
    private static readonly DateTime Now = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);

    private static ModalDialog ShownDialog(Control view, string title) =>
        view.GetLogicalDescendants().OfType<ModalDialog>().Single(d => d.Title == title && IsShown(d));

    private static IEnumerable<ModalDialog> ShownDialogs(Control view) =>
        view.GetLogicalDescendants().OfType<ModalDialog>().Where(IsShown);

    // ── Planner ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task DeletingAFinalBlock_FromItsSnapshot()
    {
        var block = new PlannedBlockDto
        {
            Id = Guid.NewGuid(), PresetId = Guid.NewGuid(), PresetName = "Deep Work", BlockPresetType = BlockPresetType.Background,
            Shown = At.Slot(new DateTime(2026, 9, 29, 5, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc)),
            IsFinal = true,
            Allowances = new BlockChangeAllowances(CanReschedule: false, CanChangePreset: false, CanSplit: false, CanDelete: true),
        };
        var queries = new FakeQueryDispatcher()
            .Answer<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>(q => q.Date == Today ? [block] : []);
        var vm = Planner(queries);
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.IsSnapshotVisible.Should().BeTrue();
        vm.RequestDeleteSnapshotBlockCommand.Execute(null);
        Settle();

        VisibleTexts(ShownDialog(view, "Delete this block?")).Should().Equal(
            "Delete this block?",
            "This permanently removes it from the Planner, along with its session record. This cannot be undone.",
            "Keep It", "Delete");

        vm.CancelDeleteSnapshotBlockCommand.Execute(null);
        Settle();
        ShownDialogs(view).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void DiscardingAContractDraft()
    {
        var vm = Planner(new FakeQueryDispatcher());
        var view = new PlannerPageView { DataContext = vm };
        Show(view);
        DriveWizardIntoDrafting(vm);
        vm.IsDraftingContract.Should().BeTrue();

        vm.RequestCancelDraftCommand.Execute(null);
        Settle();

        VisibleTexts(ShownDialog(view, "Discard Draft")).Should().Equal(
            "Discard Draft", "Discard this contract draft?", "Keep Drafting", "Discard");

        vm.CancelCancelDraftCommand.Execute(null);
        Settle();
        ShownDialogs(view).Should().BeEmpty();
        vm.IsDraftingContract.Should().BeTrue("Keep Drafting keeps the draft");
    }

    [AvaloniaFact]
    public async Task CancellingAContract_AndARefusalShowsWithOneOkButton()
    {
        var contract = new NenContractSummaryDto
        {
            Id = Guid.NewGuid(), Name = "Focused Week", StartDate = Today, EndDate = Today.AddDays(6),
            StrictnessRule = StrictnessRule.DailyFlexibility, Status = NenContractStatus.Active
        };
        var queries = new FakeQueryDispatcher()
            .Answer<GetInEffectNenContractsQuery, IReadOnlyList<NenContractSummaryDto>>(_ => [contract])
            .Answer<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto>(_ => new NenContractCompletionReadinessDto())
            .Answer<CheckNenContractCancellableQuery, ContractActionVerdictDto?>(_ => new ContractActionVerdictDto { Outcome = ContractActionOutcome.Allowed });
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<BreakNenContractCommand>(), Arg.Any<CancellationToken>())
            .Returns(BreakNenContractResult.Fail("A contract can't be cancelled while a session is running."));
        var vm = Planner(queries, commands);
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        await vm.ToggleNenContractPopoverCommand.ExecuteAsync(null);
        vm.CanCancelActiveContract.Should().BeTrue();
        vm.RequestCancelContractCommand.Execute(null);
        Settle();

        VisibleTexts(ShownDialog(view, "Cancel Contract")).Should().Equal(
            "Cancel Contract", "Are you sure you want to cancel this contract? This cannot be undone.",
            "Keep Contract", "Cancel Contract");

        await vm.ConfirmCancelContractCommand.ExecuteAsync(null);
        Settle();

        ShownDialogs(view).Select(d => d.Title).Should().Equal("Can't Cancel Contract");
        VisibleTexts(ShownDialog(view, "Can't Cancel Contract")).Should().Equal(
            "Can't Cancel Contract", "A contract can't be cancelled while a session is running.", "OK");

        vm.DismissCancelContractErrorCommand.Execute(null);
        Settle();
        ShownDialogs(view).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void DeletingAScheduledContract()
    {
        var vm = Planner(new FakeQueryDispatcher());
        var view = new PlannerPageView { DataContext = vm };
        Show(view);

        vm.RequestDeleteContractCommand.Execute(Guid.NewGuid());
        Settle();

        VisibleTexts(ShownDialog(view, "Delete Contract")).Should().Equal(
            "Delete Contract", "Delete this contract? This cannot be undone.", "Keep It", "Delete");

        vm.CancelDeleteContractCommand.Execute(null);
        Settle();
        ShownDialogs(view).Should().BeEmpty();
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task DeletingABlockPresetInUse_ShowsTheReasonInsideTheCard()
    {
        const string refusal = "This preset is assigned to one or more future planned blocks and cannot be deleted.";
        var queries = new FakeQueryDispatcher().Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ =>
            [new() { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus }]);
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<DeleteBlockPresetCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeleteBlockPresetResult.Fail(refusal));
        var vm = new BlockPresetPageViewModel(queries, commands);
        var view = new BlockPresetPageView { DataContext = vm };
        Show(view);

        vm.RequestDeletePresetCommand.Execute(vm.Presets.Single());
        Settle();
        VisibleTexts(ShownDialog(view, "Delete Preset")).Should().Equal(
            "Delete Preset", "Delete \"Deep Work\"? This cannot be undone.", "Cancel", "Delete");

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);
        Settle();

        VisibleTexts(ShownDialog(view, "Delete Preset")).Should().Equal(
            "Delete Preset", "Delete \"Deep Work\"? This cannot be undone.", refusal, "Cancel", "Delete");
    }

    [AvaloniaFact]
    public async Task DeletingAContractPresetInUse_ShowsTheReasonInsideTheCard()
    {
        const string refusal = "This preset is currently in effect for an active or scheduled Nen Contract and cannot be deleted.";
        var queries = new FakeQueryDispatcher().Answer<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>(_ =>
            [new() { Id = Guid.NewGuid(), Name = "Exam Week", StrictnessRule = StrictnessRule.Locked }]);
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<DeleteContractPresetCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeleteContractPresetResult.Fail(refusal));
        var vm = new ContractPresetPageViewModel(queries, commands);
        var view = new ContractPresetPageView { DataContext = vm };
        Show(view);

        vm.RequestDeletePresetCommand.Execute(vm.Presets.Single());
        Settle();
        VisibleTexts(ShownDialog(view, "Delete Contract Preset")).Should().Equal(
            "Delete Contract Preset", "Delete \"Exam Week\"? This cannot be undone.", "Cancel", "Delete");

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);
        Settle();

        VisibleTexts(ShownDialog(view, "Delete Contract Preset")).Should().Equal(
            "Delete Contract Preset", "Delete \"Exam Week\"? This cannot be undone.", refusal, "Cancel", "Delete");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Basics → Simple → Lockdown → Review → Begin scheduling, all on the wizard's defaults.</summary>
    internal static void DriveWizardIntoDrafting(PlannerPageViewModel vm, string name = "Focused Week")
    {
        vm.OpenContractWizardCommand.Execute(null);
        Settle();
        var wizard = vm.WizardViewModel!;
        var basics = (ContractBasicsStepViewModel)wizard.CurrentStep;
        basics.Name = name;
        basics.Goal = "Ship it";
        Settle();
        basics.ContinueCommand.Execute(null);
        ((ContractModeStepViewModel)wizard.CurrentStep).ChooseSimpleCommand.Execute(null);
        ((ContractRulesStepViewModel)wizard.CurrentStep).ContinueCommand.Execute(null);
        ((ContractReviewStepViewModel)wizard.CurrentStep).BeginSchedulingCommand.Execute(null);
        Settle();
    }

    private static PlannerPageViewModel Planner(FakeQueryDispatcher queries, ICommandDispatcher? commands = null)
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(Now);
        return new PlannerPageViewModel(queries, commands ?? Substitute.For<ICommandDispatcher>(), Substitute.For<IDisplayClock>(),
            Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), clock);
    }
}
