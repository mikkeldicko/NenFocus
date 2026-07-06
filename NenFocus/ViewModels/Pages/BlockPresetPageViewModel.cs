using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Pages;

public partial class BlockPresetCardViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string Name { get; }
    public WebsiteFilterType WebsiteFilterType { get; }
    public BlockPresetType PresetType { get; }

    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> BlockedApps { get; }
    public IReadOnlyList<string> Websites { get; }

    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;

    public string CountText =>
        $"{AppCount} app{(AppCount == 1 ? "" : "s")}  ·  {SiteCount} site{(SiteCount == 1 ? "" : "s")}";

    public BlockPresetCardViewModel(BlockPresetDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
        PresetType = dto.PresetType;
        WebsiteFilterType = dto.WebsiteFilterType;
        Tags = dto.Tags;
        BlockedApps = dto.BlockedApps;
        Websites = dto.Websites;
    }
}

public partial class BlockPresetPageViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> _getPresets;
    private readonly ICommandHandler<CreateBlockPresetCommand, CreateBlockPresetResult> _createPreset;
    private readonly ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult> _updatePreset;
    private readonly ICommandHandler<DeleteBlockPresetCommand, DeleteBlockPresetResult> _deletePreset;
    private readonly ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult> _duplicatePreset;

    private Guid? _editingPresetId;
    private readonly List<BlockPresetCardViewModel> _allPresetCards = new();
    private BlockPresetType? _activeTypeFilter;

    public ObservableCollection<BlockPresetCardViewModel> Presets { get; } = new();
    [ObservableProperty] private bool _isLoading;
    public bool HasNoPresets => Presets.Count == 0 && !IsLoading;

    // ── Type filter ────────────────────────────────────────────────────────
    public bool IsAllFilterActive => !_activeTypeFilter.HasValue;
    public bool IsFocusFilterActive => _activeTypeFilter == BlockPresetType.Focus;
    public bool IsBackgroundFilterActive => _activeTypeFilter == BlockPresetType.Background;

    private void SetFilter(BlockPresetType? type)
    {
        _activeTypeFilter = type;
        ApplyTypeFilter();
        OnPropertyChanged(nameof(IsAllFilterActive));
        OnPropertyChanged(nameof(IsFocusFilterActive));
        OnPropertyChanged(nameof(IsBackgroundFilterActive));
    }

    [RelayCommand] private void SetFilterAll() => SetFilter(null);
    [RelayCommand] private void SetFilterFocus() => SetFilter(BlockPresetType.Focus);
    [RelayCommand] private void SetFilterBackground() => SetFilter(BlockPresetType.Background);

    private void ApplyTypeFilter()
    {
        Presets.Clear();
        var filtered = _activeTypeFilter.HasValue
            ? _allPresetCards.Where(c => c.PresetType == _activeTypeFilter.Value)
            : (IEnumerable<BlockPresetCardViewModel>)_allPresetCards;
        foreach (var c in filtered) Presets.Add(c);
        OnPropertyChanged(nameof(HasNoPresets));
    }

    // ── Type picker modal ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isTypePickerVisible;

    [RelayCommand]
    private void SelectFocusType()
    {
        FormPresetType = BlockPresetType.Focus;
        IsTypePickerVisible = false;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void SelectBackgroundType()
    {
        FormPresetType = BlockPresetType.Background;
        IsTypePickerVisible = false;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void CancelTypePicker() => IsTypePickerVisible = false;

    // ── Form state ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Preset";
    [ObservableProperty] private string? _formError;

    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _newTagText = string.Empty;
    public ObservableCollection<string> FormTags { get; } = new();

    [ObservableProperty] private int _activeTab;
    public bool IsWebsiteTab => ActiveTab == 0;
    public bool IsAppTab => ActiveTab == 1;
    partial void OnActiveTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsWebsiteTab));
        OnPropertyChanged(nameof(IsAppTab));
    }

    [ObservableProperty] private bool _isAllowlist;
    [ObservableProperty] private string _newSiteText = string.Empty;
    [ObservableProperty] private string? _selectedSite;
    public ObservableCollection<string> FormSites { get; } = new();

    public string WebsiteFilterTypeHint => IsAllowlist
        ? "Only listed sites will be accessible. All others are blocked."
        : "Listed sites will be blocked during the session.";

    partial void OnIsAllowlistChanged(bool value) =>
        OnPropertyChanged(nameof(WebsiteFilterTypeHint));

    [ObservableProperty] private string? _selectedApp;
    public ObservableCollection<string> FormApps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFocusPresetType))]
    [NotifyPropertyChangedFor(nameof(IsBackgroundPresetType))]
    private BlockPresetType _formPresetType;

    public bool IsFocusPresetType => FormPresetType == BlockPresetType.Focus;
    public bool IsBackgroundPresetType => FormPresetType == BlockPresetType.Background;

    [RelayCommand] private void SetPresetTypeFocus() => FormPresetType = BlockPresetType.Focus;
    [RelayCommand] private void SetPresetTypeBackground() => FormPresetType = BlockPresetType.Background;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Preset";
    partial void OnIsEditingChanged(bool value) =>
        OnPropertyChanged(nameof(SaveButtonLabel));

    [ObservableProperty] private bool _isConfirmDeleteVisible;
    [ObservableProperty] private string _confirmDeleteMessage = string.Empty;
    [ObservableProperty] private string? _confirmDeleteError;
    private BlockPresetCardViewModel? _pendingDeleteCard;

    public BlockPresetPageViewModel(
        IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> getPresets,
        ICommandHandler<CreateBlockPresetCommand, CreateBlockPresetResult> createPreset,
        ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult> updatePreset,
        ICommandHandler<DeleteBlockPresetCommand, DeleteBlockPresetResult> deletePreset,
        ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult> duplicatePreset)
    {
        _getPresets = getPresets;
        _createPreset = createPreset;
        _updatePreset = updatePreset;
        _deletePreset = deletePreset;
        _duplicatePreset = duplicatePreset;

        Presets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoPresets));
        _ = LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        IsLoading = true;
        var presets = await _getPresets.HandleAsync(new GetAllBlockPresetsQuery());
        _allPresetCards.Clear();
        foreach (var p in presets)
            _allPresetCards.Add(new BlockPresetCardViewModel(p));
        ApplyTypeFilter();
        IsLoading = false;
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormPresetType = BlockPresetType.Focus;
        IsAllowlist = false;
        ActiveTab = 0;
        FormTags.Clear();
        FormSites.Clear();
        FormApps.Clear();
        NewTagText = string.Empty;
        NewSiteText = string.Empty;
        SelectedSite = null;
        SelectedApp = null;
        FormError = null;
    }

    [RelayCommand]
    private void NewPreset()
    {
        _editingPresetId = null;
        IsEditing = false;
        FormTitle = "New Preset";
        ResetForm();

        if (_activeTypeFilter.HasValue)
        {
            FormPresetType = _activeTypeFilter.Value;
            IsFormVisible = true;
        }
        else
        {
            IsTypePickerVisible = true;
        }
    }

    [RelayCommand]
    private void EditPreset(BlockPresetCardViewModel card)
    {
        _editingPresetId = card.Id;
        IsEditing = true;
        FormTitle = "Edit Preset";
        ResetForm();
        FormName = card.Name;
        FormPresetType = card.PresetType;
        IsAllowlist = card.WebsiteFilterType == WebsiteFilterType.Allowlist;
        foreach (var t in card.Tags) FormTags.Add(t);
        foreach (var s in card.Websites) FormSites.Add(s);
        foreach (var a in card.BlockedApps) FormApps.Add(a);
        IsFormVisible = true;
    }

    // ── Duplicate ─────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task DuplicatePresetAsync(BlockPresetCardViewModel card)
    {
        var result = await _duplicatePreset.HandleAsync(
            new DuplicateBlockPresetCommand { SourcePresetId = card.Id });
        if (result.Success)
            await LoadPresetsAsync();
    }

    // ── Delete confirmation ───────────────────────────────────────────────
    [RelayCommand]
    private void RequestDeletePreset(BlockPresetCardViewModel card)
    {
        _pendingDeleteCard = card;
        ConfirmDeleteMessage = $"Delete \"{card.Name}\"? This cannot be undone.";
        ConfirmDeleteError = null;
        IsConfirmDeleteVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteCard is null) return;
        var result = await _deletePreset.HandleAsync(
            new DeleteBlockPresetCommand { PresetId = _pendingDeleteCard.Id });
        if (!result.Success)
        {
            ConfirmDeleteError = result.ErrorMessage;
            return;
        }
        _allPresetCards.Remove(_pendingDeleteCard);
        ApplyTypeFilter();
        _pendingDeleteCard = null;
        IsConfirmDeleteVisible = false;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteCard = null;
        ConfirmDeleteError = null;
        IsConfirmDeleteVisible = false;
    }

    [RelayCommand] private void ShowWebsiteTab() => ActiveTab = 0;
    [RelayCommand] private void ShowAppTab() => ActiveTab = 1;

    [RelayCommand] private void SetBlocklist() => IsAllowlist = false;
    [RelayCommand] private void SetAllowlist() => IsAllowlist = true;

    [RelayCommand]
    private void AddTag()
    {
        var t = NewTagText.Trim();
        if (!string.IsNullOrEmpty(t) && !FormTags.Contains(t, StringComparer.OrdinalIgnoreCase))
            FormTags.Add(t);
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private void RemoveTag(string tag) => FormTags.Remove(tag);

    [RelayCommand]
    private void AddSite()
    {
        var s = NewSiteText.Trim();
        if (!string.IsNullOrEmpty(s) && !FormSites.Contains(s, StringComparer.OrdinalIgnoreCase))
            FormSites.Add(s);
        NewSiteText = string.Empty;
    }

    [RelayCommand]
    private void RemoveSelectedSite()
    {
        if (SelectedSite is not null)
        {
            FormSites.Remove(SelectedSite);
            SelectedSite = null;
        }
    }

    public void AddAppFromExePath(string processName)
    {
        if (!string.IsNullOrEmpty(processName) &&
            !FormApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
            FormApps.Add(processName);
    }

    [RelayCommand]
    private void RemoveSelectedApp()
    {
        if (SelectedApp is not null)
        {
            FormApps.Remove(SelectedApp);
            SelectedApp = null;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        var name = FormName.Trim();

        if (string.IsNullOrEmpty(name))
        {
            FormError = "Preset name is required.";
            return;
        }

        var policy = IsAllowlist ? WebsiteFilterType.Allowlist : WebsiteFilterType.Blocklist;

        if (IsEditing && _editingPresetId.HasValue)
        {
            var result = await _updatePreset.HandleAsync(new UpdateBlockPresetCommand
            {
                PresetId = _editingPresetId.Value,
                Name = name,
                Tags = FormTags.ToList(),
                WebsiteFilterType = policy,
                PresetType = FormPresetType,
                BlockedApps = FormApps.ToList(),
                Websites = FormSites.ToList()
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _createPreset.HandleAsync(new CreateBlockPresetCommand
            {
                Name = name,
                Tags = FormTags.ToList(),
                WebsiteFilterType = policy,
                PresetType = FormPresetType,
                BlockedApps = FormApps.ToList(),
                Websites = FormSites.ToList()
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }

        await LoadPresetsAsync();
        CloseForm();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingPresetId = null;
    }
}
