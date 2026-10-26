using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetInEffectNenContracts;
using NenFocus.Application.Queries.GetBlocksForDay;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Planner;
using NenFocus.Views.Planner;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice B stopped building draft tiles as fake block DTOs. This drives the real Planner on
/// the real composition root into drafting through the contract wizard, places one Focus and one
/// Background block, moves and reopens one, and seals: both tiles show with their preset names
/// while drafting, and Seal saves both blocks under the new contract.
/// </summary>
public class ContractDraftingViewTests : IAsyncDisposable
{
    // Monday 07:00 UTC, zone UTC: the wizard drafts Monday … Sunday of this week.
    private static readonly DateTime Now = new(2026, 10, 26, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Tuesday = new(2026, 10, 27);
    private readonly RealApp _app = new(Now);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [AvaloniaFact]
    public async Task DraftTiles_ShowWithTheirPresetNames_MoveAndEdit_AndSealSavesThem()
    {
        var focusPresetId = await _app.SeedPresetAsync("Thesis Focus", BlockPresetType.Focus);
        var backgroundPresetId = await _app.SeedPresetAsync("Evening Calm", BlockPresetType.Background);
        var vm = new PlannerPageViewModel(_app.Queries, _app.Commands, Substitute.For<IDisplayClock>(),
            _app.Provider.GetRequiredService<IActiveSessionState>(), _app.Provider.GetRequiredService<IRecapRelay>(), _app.Clock);
        var view = new PlannerPageView { DataContext = vm };
        Show(view);
        await RealApp.SettleAsync();

        ConfirmDialogViewTests.DriveWizardIntoDrafting(vm, name: "Draft Week");
        await RealApp.SettleAsync();
        vm.IsDraftingContract.Should().BeTrue();

        // Tuesday 13:00–14:00 Focus, Tuesday 15:00–16:00 Background.
        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 13));
        vm.SetFocusBlockCommand.Execute(null);
        vm.SelectedPreset = vm.AvailablePresets.Single(p => p.Id == focusPresetId);
        await vm.SaveCommand.ExecuteAsync(null);
        vm.FormError.Should().BeNull();

        await vm.NewBlockAtCommand.ExecuteAsync(new GridSlotPosition(ColumnIndex: 1, Hour: 15));
        vm.SetBackgroundBlockCommand.Execute(null);
        vm.SelectedPreset = vm.AvailablePresets.Single(p => p.Id == backgroundPresetId);
        await vm.SaveCommand.ExecuteAsync(null);
        vm.FormError.Should().BeNull();
        await RealApp.SettleAsync();

        vm.Tiles.Should().HaveCount(2).And.OnlyContain(t => t.IsDraft);
        vm.Tiles.Select(t => (t.PresetName, t.BlockPresetType)).Should().BeEquivalentTo(
            [("Thesis Focus", BlockPresetType.Focus), ("Evening Calm", BlockPresetType.Background)]);
        VisibleTexts(view).Should().Contain(["Thesis Focus", "Evening Calm"]);

        // A draft tile can be dragged (never locked) and reopened in the form.
        var focusTile = vm.Tiles.Single(t => t.IsFocus);
        vm.IsTileFullyLocked(focusTile.Id).Should().BeFalse();
        await vm.MoveOrResizeBlockCommand.ExecuteAsync(new DragMoveResult(
            focusTile.Id, new DateTime(2026, 10, 27, 10, 0, 0), new DateTime(2026, 10, 27, 11, 0, 0)));
        vm.DragError.Should().BeNull();
        focusTile = vm.Tiles.Single(t => t.IsFocus);
        focusTile.Start.Should().Be(new DateTime(2026, 10, 27, 10, 0, 0));

        await vm.EditBlockCommand.ExecuteAsync(focusTile);
        vm.IsFormVisible.Should().BeTrue();
        vm.FormStartTime.Should().Be(new TimeSpan(10, 0, 0));
        vm.SelectedPreset!.Id.Should().Be(focusPresetId);
        vm.CloseFormCommand.Execute(null);

        await vm.SealContractCommand.ExecuteAsync(null);
        await RealApp.SettleAsync();

        vm.SealErrorMessage.Should().BeNull();
        vm.IsDraftingContract.Should().BeFalse();
        var contract = (await _app.Queries.QueryAsync(new GetInEffectNenContractsQuery())).Single();
        contract.Name.Should().Be("Draft Week");
        var saved = await _app.Queries.QueryAsync(new GetBlocksForDayQuery { Date = Tuesday });
        saved.Select(b => (b.PresetName, b.ContractId)).Should().BeEquivalentTo(
            [("Thesis Focus", (Guid?)contract.Id), ("Evening Calm", (Guid?)contract.Id)]);
        vm.Tiles.Should().HaveCount(2).And.OnlyContain(t => !t.IsDraft, "the sealed blocks are real blocks now");
    }
}
