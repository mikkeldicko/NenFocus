using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>The Block Presets and Contract Presets pages, rendered for real.</summary>
public class PresetPageViewTests
{
    [AvaloniaFact]
    public void BlockPresets_ListsPresetsAndTheTypeFilterNarrowsThem()
    {
        var queries = new FakeQueryDispatcher().Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ =>
        [
            new() { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus, BlockedApps = ["discord"] },
            new() { Id = Guid.NewGuid(), Name = "Evenings", PresetType = BlockPresetType.Background, Websites = ["reddit.com"] }
        ]);
        var view = new BlockPresetPageView { DataContext = new BlockPresetPageViewModel(queries, Substitute.For<ICommandDispatcher>()) };
        var window = Show(view);

        VisibleTexts(view).Should().Contain(["Deep Work", "Evenings"]);

        Click(window, ButtonWithText(view, "Focus"));
        VisibleTexts(view).Should().Contain("Deep Work").And.NotContain("Evenings");

        Click(window, ButtonWithText(view, "All"));
        VisibleTexts(view).Should().Contain(["Deep Work", "Evenings"]);
    }

    [AvaloniaFact]
    public void BlockPresets_NewPresetWithNoFilterAsksForTheTypeFirst()
    {
        var vm = new BlockPresetPageViewModel(new FakeQueryDispatcher(), Substitute.For<ICommandDispatcher>());
        var view = new BlockPresetPageView { DataContext = vm };
        Show(view);

        vm.NewPresetCommand.Execute(null);
        Settle();

        vm.IsTypePickerVisible.Should().BeTrue();
        vm.IsFormVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ContractPresets_EditShowsTheStrictnessAndTheComboBoxWritesBack()
    {
        var preset = new ContractPresetDto
        {
            Id = Guid.NewGuid(), Name = "Exam Week", StrictnessRule = StrictnessRule.Locked,
            AllowCancellation = false, RedemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract
        };
        var queries = new FakeQueryDispatcher().Answer<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>(_ => [preset]);
        var vm = new ContractPresetPageViewModel(queries, Substitute.For<ICommandDispatcher>());
        var view = new ContractPresetPageView { DataContext = vm };
        Show(view);

        VisibleTexts(view).Should().Contain(["Exam Week", "Lockdown", "No cancellation"]);

        vm.EditPresetCommand.Execute(vm.Presets.Single());
        Settle();

        var strictness = view.GetLogicalDescendants().OfType<ComboBox>().First(IsShown);
        strictness.SelectedItem.Should().Be(StrictnessRule.Locked);
        VisibleTexts(strictness).Should().Contain("Lockdown");

        strictness.SelectedIndex = Array.IndexOf(Enum.GetValues<StrictnessRule>(), StrictnessRule.PeriodTimeBudget);
        Settle();
        vm.FormStrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        VisibleTexts(strictness).Should().Contain("Period Study");
    }
}
