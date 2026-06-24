using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Pages;

public partial class BlockProfileCardViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string Name { get; }
    public WebsitePolicy WebsitePolicy { get; }
    public BlockType ProfileType { get; }

    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> BlockedApps { get; }
    public IReadOnlyList<string> Websites { get; }

    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;

    public string CountText =>
        $"{AppCount} app{(AppCount == 1 ? "" : "s")}  ·  {SiteCount} site{(SiteCount == 1 ? "" : "s")}";

    public BlockProfileCardViewModel(BlockProfileDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
        ProfileType = dto.ProfileType;
        WebsitePolicy = dto.WebsitePolicy;
        Tags = dto.Tags;
        BlockedApps = dto.BlockedApps;
        Websites = dto.Websites;
    }
}

public partial class BlockProfilePageViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _getProfiles;
    private readonly ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult> _createProfile;
    private readonly ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult> _updateProfile;
    private readonly ICommandHandler<DeleteBlockProfileCommand, DeleteBlockProfileResult> _deleteProfile;
    private readonly ICommandHandler<DuplicateBlockProfileCommand, DuplicateBlockProfileResult> _duplicateProfile;

    private Guid? _editingProfileId;
    private readonly List<BlockProfileCardViewModel> _allProfileCards = new();
    private BlockType? _activeTypeFilter;

    public ObservableCollection<BlockProfileCardViewModel> Profiles { get; } = new();
    [ObservableProperty] private bool _isLoading;
    public bool HasNoProfiles => Profiles.Count == 0 && !IsLoading;

    // ── Type filter ────────────────────────────────────────────────────────
    public bool IsAllFilterActive => !_activeTypeFilter.HasValue;
    public bool IsFocusFilterActive => _activeTypeFilter == BlockType.Focus;
    public bool IsBackgroundFilterActive => _activeTypeFilter == BlockType.Background;

    private void SetFilter(BlockType? type)
    {
        _activeTypeFilter = type;
        ApplyTypeFilter();
        OnPropertyChanged(nameof(IsAllFilterActive));
        OnPropertyChanged(nameof(IsFocusFilterActive));
        OnPropertyChanged(nameof(IsBackgroundFilterActive));
    }

    [RelayCommand] private void SetFilterAll() => SetFilter(null);
    [RelayCommand] private void SetFilterFocus() => SetFilter(BlockType.Focus);
    [RelayCommand] private void SetFilterBackground() => SetFilter(BlockType.Background);

    private void ApplyTypeFilter()
    {
        Profiles.Clear();
        var filtered = _activeTypeFilter.HasValue
            ? _allProfileCards.Where(c => c.ProfileType == _activeTypeFilter.Value)
            : (IEnumerable<BlockProfileCardViewModel>)_allProfileCards;
        foreach (var c in filtered) Profiles.Add(c);
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    // ── Type picker modal ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isTypePickerVisible;

    [RelayCommand]
    private void SelectFocusType()
    {
        FormProfileType = BlockType.Focus;
        IsTypePickerVisible = false;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void SelectBackgroundType()
    {
        FormProfileType = BlockType.Background;
        IsTypePickerVisible = false;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void CancelTypePicker() => IsTypePickerVisible = false;

    // ── Form state ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Profile";
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

    public string WebsitePolicyHint => IsAllowlist
        ? "Only listed sites will be accessible. All others are blocked."
        : "Listed sites will be blocked during the session.";

    partial void OnIsAllowlistChanged(bool value) =>
        OnPropertyChanged(nameof(WebsitePolicyHint));

    [ObservableProperty] private string? _selectedApp;
    public ObservableCollection<string> FormApps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFocusProfileType))]
    [NotifyPropertyChangedFor(nameof(IsBackgroundProfileType))]
    private BlockType _formProfileType;

    public bool IsFocusProfileType => FormProfileType == BlockType.Focus;
    public bool IsBackgroundProfileType => FormProfileType == BlockType.Background;

    [RelayCommand] private void SetProfileTypeFocus() => FormProfileType = BlockType.Focus;
    [RelayCommand] private void SetProfileTypeBackground() => FormProfileType = BlockType.Background;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Profile";
    partial void OnIsEditingChanged(bool value) =>
        OnPropertyChanged(nameof(SaveButtonLabel));

    [ObservableProperty] private bool _isConfirmDeleteVisible;
    [ObservableProperty] private string _confirmDeleteMessage = string.Empty;
    [ObservableProperty] private string? _confirmDeleteError;
    private BlockProfileCardViewModel? _pendingDeleteCard;

    public BlockProfilePageViewModel(
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> getProfiles,
        ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult> createProfile,
        ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult> updateProfile,
        ICommandHandler<DeleteBlockProfileCommand, DeleteBlockProfileResult> deleteProfile,
        ICommandHandler<DuplicateBlockProfileCommand, DuplicateBlockProfileResult> duplicateProfile)
    {
        _getProfiles = getProfiles;
        _createProfile = createProfile;
        _updateProfile = updateProfile;
        _deleteProfile = deleteProfile;
        _duplicateProfile = duplicateProfile;

        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));
        _ = LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        var profiles = await _getProfiles.HandleAsync(new GetAllBlockProfilesQuery());
        _allProfileCards.Clear();
        foreach (var p in profiles)
            _allProfileCards.Add(new BlockProfileCardViewModel(p));
        ApplyTypeFilter();
        IsLoading = false;
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormProfileType = BlockType.Focus;
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
    private void NewProfile()
    {
        _editingProfileId = null;
        IsEditing = false;
        FormTitle = "New Profile";
        ResetForm();

        if (_activeTypeFilter.HasValue)
        {
            FormProfileType = _activeTypeFilter.Value;
            IsFormVisible = true;
        }
        else
        {
            IsTypePickerVisible = true;
        }
    }

    [RelayCommand]
    private void EditProfile(BlockProfileCardViewModel card)
    {
        _editingProfileId = card.Id;
        IsEditing = true;
        FormTitle = "Edit Profile";
        ResetForm();
        FormName = card.Name;
        FormProfileType = card.ProfileType;
        IsAllowlist = card.WebsitePolicy == WebsitePolicy.Allowlist;
        foreach (var t in card.Tags) FormTags.Add(t);
        foreach (var s in card.Websites) FormSites.Add(s);
        foreach (var a in card.BlockedApps) FormApps.Add(a);
        IsFormVisible = true;
    }

    // ── Duplicate ─────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task DuplicateProfileAsync(BlockProfileCardViewModel card)
    {
        var result = await _duplicateProfile.HandleAsync(
            new DuplicateBlockProfileCommand { SourceProfileId = card.Id });
        if (result.Success)
            await LoadProfilesAsync();
    }

    // ── Delete confirmation ───────────────────────────────────────────────
    [RelayCommand]
    private void RequestDeleteProfile(BlockProfileCardViewModel card)
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
        var result = await _deleteProfile.HandleAsync(
            new DeleteBlockProfileCommand { ProfileId = _pendingDeleteCard.Id });
        if (!result.Success)
        {
            ConfirmDeleteError = result.ErrorMessage;
            return;
        }
        _allProfileCards.Remove(_pendingDeleteCard);
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
            FormError = "Profile name is required.";
            return;
        }

        var policy = IsAllowlist ? WebsitePolicy.Allowlist : WebsitePolicy.Blocklist;

        if (IsEditing && _editingProfileId.HasValue)
        {
            var result = await _updateProfile.HandleAsync(new UpdateBlockProfileCommand
            {
                ProfileId = _editingProfileId.Value,
                Name = name,
                Tags = FormTags.ToList(),
                WebsitePolicy = policy,
                ProfileType = FormProfileType,
                BlockedApps = FormApps.ToList(),
                Websites = FormSites.ToList()
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _createProfile.HandleAsync(new CreateBlockProfileCommand
            {
                Name = name,
                Tags = FormTags.ToList(),
                WebsitePolicy = policy,
                ProfileType = FormProfileType,
                BlockedApps = FormApps.ToList(),
                Websites = FormSites.ToList()
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }

        await LoadProfilesAsync();
        CloseForm();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingProfileId = null;
    }
}
