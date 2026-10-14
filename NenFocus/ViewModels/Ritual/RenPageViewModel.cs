using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.ViewModels.Ritual;

public partial class RenPresetCardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public BlockPresetDto Preset { get; }

    public string SitesLabel => Preset.WebsiteFilterType == WebsiteFilterType.Allowlist
        ? "ALLOWED SITES"
        : "BLOCKED SITES";

    public RenPresetCardViewModel(BlockPresetDto preset)
    {
        Preset = preset;
    }
}

public partial class RenPageViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    public SessionOrigin Mode { get; }
    public bool IsPlannedMode => Mode == SessionOrigin.Planned;

    // Spontaneous mode: list of selectable preset cards.
    public ObservableCollection<RenPresetCardViewModel> Presets { get; } = new();

    // Planned mode: the single locked card.
    public RenPresetCardViewModel? LockedPresetCard { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPreset))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    [NotifyPropertyChangedFor(nameof(IsAllowlistMode))]
    [NotifyPropertyChangedFor(nameof(CanAddSites))]
    [NotifyPropertyChangedFor(nameof(IsPresetSelected))]
    [NotifyPropertyChangedFor(nameof(ShowExtraBlockingPanel))]
    private RenPresetCardViewModel? _selectedPresetCard;

    public BlockPresetDto? SelectedPreset => IsPlannedMode
        ? LockedPresetCard?.Preset
        : SelectedPresetCard?.Preset;

    public bool HasSelectedPreset => SelectedPreset is not null;
    public bool IsAllowlistMode => SelectedPreset?.WebsiteFilterType == WebsiteFilterType.Allowlist;

    // True when the user has picked a preset in spontaneous mode.
    public bool IsPresetSelected => !IsPlannedMode && SelectedPresetCard is not null;

    // Whether the extra-blocking panel (add sites/apps) is available.
    public bool ShowExtraBlockingPanel => IsPlannedMode || IsPresetSelected;
    public bool CanAddSites => ShowExtraBlockingPanel && !IsAllowlistMode;

    // Additional session-only blocked items (Planned mode only).
    public ObservableCollection<string> AdditionalBlockedSites { get; } = new();
    public ObservableCollection<string> AdditionalBlockedApps { get; } = new();

    [ObservableProperty]
    private string _addSiteInput = string.Empty;

    [ObservableProperty]
    private string? _addSiteError;

    [ObservableProperty]
    private bool _isLoading = true;

    public RenPageViewModel(
        IQueryDispatcher queryDispatcher,
        SessionOrigin mode,
        BlockPresetDto? lockedPreset,
        Action onContinue,
        Action onBack)
    {
        _queryDispatcher = queryDispatcher;
        Mode = mode;
        _onContinue = onContinue;
        _onBack = onBack;

        if (mode == SessionOrigin.Planned && lockedPreset is not null)
        {
            LockedPresetCard = new RenPresetCardViewModel(lockedPreset);
            IsLoading = false;
        }
        else
        {
            _ = LoadPresetsAsync();
        }
    }

    private async Task LoadPresetsAsync()
    {
        IsLoading = true;
        try
        {
            var presets = await _queryDispatcher.QueryAsync(new GetAllBlockPresetsQuery { PresetType = BlockPresetType.Focus });
            Presets.Clear();
            foreach (var p in presets)
                Presets.Add(new RenPresetCardViewModel(p));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectPreset(RenPresetCardViewModel card)
    {
        if (SelectedPresetCard is not null)
            SelectedPresetCard.IsSelected = false;
        card.IsSelected = true;
        SelectedPresetCard = card;
    }

    [RelayCommand]
    private void ChangePreset()
    {
        if (SelectedPresetCard is not null)
            SelectedPresetCard.IsSelected = false;
        SelectedPresetCard = null;
        AdditionalBlockedSites.Clear();
        AdditionalBlockedApps.Clear();
        AddSiteInput = string.Empty;
        AddSiteError = null;
    }

    [RelayCommand]
    private void ToggleSelectedCardExpanded()
    {
        if (SelectedPresetCard is not null)
            SelectedPresetCard.IsExpanded = !SelectedPresetCard.IsExpanded;
    }

    [RelayCommand]
    private void ToggleLockedPresetExpanded()
    {
        if (LockedPresetCard is not null)
            LockedPresetCard.IsExpanded = !LockedPresetCard.IsExpanded;
    }

    [RelayCommand]
    private void AddSite()
    {
        AddSiteError = null;
        var raw = AddSiteInput.Trim();
        if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = raw["https://".Length..];
        else if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            raw = raw["http://".Length..];
        if (raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            raw = raw["www.".Length..];
        raw = raw.Split('/')[0].Trim();

        if (string.IsNullOrEmpty(raw))
        {
            AddSiteError = "Please enter a website address.";
            return;
        }

        // Duplicate check against the locked preset's existing sites.
        if (LockedPresetCard is not null)
        {
            foreach (var s in LockedPresetCard.Preset.Websites)
            {
                if (string.Equals(s, raw, StringComparison.OrdinalIgnoreCase))
                {
                    AddSiteError = $"\"{raw}\" is already in this preset.";
                    return;
                }
            }
        }

        // Duplicate check against already-added session sites.
        foreach (var s in AdditionalBlockedSites)
        {
            if (string.Equals(s, raw, StringComparison.OrdinalIgnoreCase))
            {
                AddSiteError = $"\"{raw}\" has already been added.";
                return;
            }
        }

        AdditionalBlockedSites.Add(raw);
        AddSiteInput = string.Empty;
    }

    [RelayCommand]
    private void RemoveAdditionalSite(string site) => AdditionalBlockedSites.Remove(site);

    // Called from the view's code-behind after the file picker resolves an .exe path.
    public void AddAppFromPath(string exePath)
    {
        // BlockedApp owns what an app's name is (Gap 112); whether it may be blocked is Start's answer.
        var name = BlockedApp.NameOf(exePath);
        if (name.Length == 0) return;

        foreach (var a in AdditionalBlockedApps)
        {
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return;
        }

        if (LockedPresetCard is not null)
        {
            foreach (var a in LockedPresetCard.Preset.BlockedApps)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return;
            }
        }

        AdditionalBlockedApps.Add(name);
    }

    [RelayCommand]
    private void RemoveAdditionalApp(string app) => AdditionalBlockedApps.Remove(app);

    [RelayCommand]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back()
    {
        if (!IsPlannedMode && SelectedPresetCard is not null)
        {
            SelectedPresetCard.IsSelected = false;
            SelectedPresetCard = null;
        }
        _onBack();
    }
}
