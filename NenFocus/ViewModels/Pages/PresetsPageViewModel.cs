using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Pages;

/// <summary>
/// Thin tab switcher over two independent entity-type CRUD pages — owns no state of its own
/// beyond which tab is active. Mirrors ShellViewModel.CurrentPage's own "hold a ViewModelBase,
/// let the global ViewLocator resolve its View" shape, one level down.
/// </summary>
public partial class PresetsPageViewModel : ViewModelBase
{
    public BlockPresetPageViewModel BlockPresets { get; }
    public ContractPresetPageViewModel ContractPresets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePage))]
    [NotifyPropertyChangedFor(nameof(IsBlockPresetsTabActive))]
    [NotifyPropertyChangedFor(nameof(IsContractPresetsTabActive))]
    private bool _isContractPresetsTab;

    public bool IsBlockPresetsTabActive => !IsContractPresetsTab;
    public bool IsContractPresetsTabActive => IsContractPresetsTab;

    public ViewModelBase ActivePage => IsContractPresetsTab ? ContractPresets : BlockPresets;

    public PresetsPageViewModel(BlockPresetPageViewModel blockPresets, ContractPresetPageViewModel contractPresets)
    {
        BlockPresets = blockPresets;
        ContractPresets = contractPresets;
    }

    [RelayCommand] private void ShowBlockPresetsTab() => IsContractPresetsTab = false;
    [RelayCommand] private void ShowContractPresetsTab() => IsContractPresetsTab = true;
}
