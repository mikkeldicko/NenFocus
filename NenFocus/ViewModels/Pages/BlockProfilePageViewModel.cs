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
    public string WebsitePolicy { get; }

    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> BlockedApps { get; }
    public IReadOnlyList<string> Websites { get; }

    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;

    public string CountText =>
        $"{AppCount} app{(AppCount == 1 ? "" : "s")}  Â·  {SiteCount} site{(SiteCount == 1 ? "" : "s")}";

    public BlockProfileCardViewModel(BlockProfileDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
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

    // â”€â”€ Profile list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public ObservableCollection<BlockProfileCardViewModel> Profiles { get; } = new();
    [ObservableProperty] private bool _isLoading;
    public bool HasNoProfiles => Profiles.Count == 0 && !IsLoading;

    // â”€â”€ Modal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Profile";
    [ObservableProperty] private string? _formError;

    // â”€â”€ Name + tags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _newTagText = string.Empty;
    public ObservableCollection<string> FormTags { get; } = new();

    // â”€â”€ Tabs (0 = Websites, 1 = Apps) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private int _activeTab;
    public bool IsWebsiteTab => ActiveTab == 0;
    public bool IsAppTab => ActiveTab == 1;
    partial void OnActiveTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsWebsiteTab));
        OnPropertyChanged(nameof(IsAppTab));
    }

    // â”€â”€ Websites tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private bool _isAllowlist;
    [ObservableProperty] private string _newSiteText = string.Empty;
    [ObservableProperty] private string? _selectedSite;
    public ObservableCollection<string> FormSites { get; } = new();

    public string WebsitePolicyHint => IsAllowlist
        ? "Only listed sites will be accessible. All others are blocked."
        : "Listed sites will be blocked during the session.";

    partial void OnIsAllowlistChanged(bool value) =>
        OnPropertyChanged(nameof(WebsitePolicyHint));

    // â”€â”€ Apps tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private string? _selectedApp;
    public ObservableCollection<string> FormApps { get; } = new();

    // â”€â”€ Save button label â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Profile";
    partial void OnIsEditingChanged(bool value) =>
        OnPropertyChanged(nameof(SaveButtonLabel));

    // â”€â”€ Delete confirmation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [ObservableProperty] private bool _isConfirmDeleteVisible;
    [ObservableProperty] private string _confirmDeleteMessage = string.Empty;
    private BlockProfileCardViewModel? _pendingDeleteCard;

    // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ Load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        var profiles = await _getProfiles.HandleAsync(new GetAllBlockProfilesQuery());
        Profiles.Clear();
        foreach (var p in profiles)
            Profiles.Add(new BlockProfileCardViewModel(p));
        IsLoading = false;
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    // â”€â”€ Form helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void ResetForm()
    {
        FormName = string.Empty;
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

    // â”€â”€ New profile â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand]
    private void NewProfile()
    {
        _editingProfileId = null;
        IsEditing = false;
        FormTitle = "New Profile";
        ResetForm();
        IsFormVisible = true;
    }

    // â”€â”€ Edit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand]
    private void EditProfile(BlockProfileCardViewModel card)
    {
        _editingProfileId = card.Id;
        IsEditing = true;
        FormTitle = "Edit Profile";
        ResetForm();
        FormName = card.Name;
        IsAllowlist = card.WebsitePolicy == "Allowlist";
        foreach (var t in card.Tags) FormTags.Add(t);
        foreach (var s in card.Websites) FormSites.Add(s);
        foreach (var a in card.BlockedApps) FormApps.Add(a);
        IsFormVisible = true;
    }

    // â”€â”€ Duplicate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand]
    private async Task DuplicateProfileAsync(BlockProfileCardViewModel card)
    {
        var result = await _duplicateProfile.HandleAsync(
            new DuplicateBlockProfileCommand { SourceProfileId = card.Id });
        if (result.Success)
            await LoadProfilesAsync();
    }

    // â”€â”€ Delete confirmation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand]
    private void RequestDeleteProfile(BlockProfileCardViewModel card)
    {
        _pendingDeleteCard = card;
        ConfirmDeleteMessage = $"Delete \"{card.Name}\"? This cannot be undone.";
        IsConfirmDeleteVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteCard is null) return;
        await _deleteProfile.HandleAsync(
            new DeleteBlockProfileCommand { ProfileId = _pendingDeleteCard.Id });
        Profiles.Remove(_pendingDeleteCard);
        _pendingDeleteCard = null;
        IsConfirmDeleteVisible = false;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteCard = null;
        IsConfirmDeleteVisible = false;
    }

    // â”€â”€ Tab switching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand] private void ShowWebsiteTab() => ActiveTab = 0;
    [RelayCommand] private void ShowAppTab() => ActiveTab = 1;

    // â”€â”€ Policy toggle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand] private void SetBlocklist() => IsAllowlist = false;
    [RelayCommand] private void SetAllowlist() => IsAllowlist = true;

    // â”€â”€ Tags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ Sites â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ Apps â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // â”€â”€ Browse for app (called from code-behind) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ Save â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                BlockedApps = FormApps.ToList(),
                Websites = FormSites.ToList()
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }

        await LoadProfilesAsync();
        CloseForm();
    }

    // â”€â”€ Close â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingProfileId = null;
    }
}
