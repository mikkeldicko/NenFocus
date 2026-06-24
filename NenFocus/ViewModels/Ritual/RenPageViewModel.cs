using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Domain.Enums;
using NenFocus.Enums;

namespace NenFocus.ViewModels.Ritual;

public partial class RenProfileCardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public BlockProfileDto Profile { get; }

    public RenProfileCardViewModel(BlockProfileDto profile)
    {
        Profile = profile;
    }
}

public partial class RenPageViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>>? _profilesQuery;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    public RitualMode Mode { get; }
    public bool IsPlannedMode => Mode == RitualMode.Planned;

    // Spontaneous mode: list of selectable profile cards.
    public ObservableCollection<RenProfileCardViewModel> Profiles { get; } = new();

    // Planned mode: the single locked card.
    public RenProfileCardViewModel? LockedProfileCard { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfile))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(IsAllowlistMode))]
    [NotifyPropertyChangedFor(nameof(AllowlistHintVisible))]
    [NotifyPropertyChangedFor(nameof(CanAddSites))]
    private RenProfileCardViewModel? _selectedProfileCard;

    public BlockProfileDto? SelectedProfile => IsPlannedMode
        ? LockedProfileCard?.Profile
        : SelectedProfileCard?.Profile;

    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool IsAllowlistMode => SelectedProfile?.WebsitePolicy == WebsitePolicy.Allowlist;
    public bool AllowlistHintVisible => IsAllowlistMode;

    // In Planned mode, sites cannot be added when the profile is Allowlist.
    public bool CanAddSites => IsPlannedMode && !IsAllowlistMode;

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
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>>? profilesQuery,
        RitualMode mode,
        BlockProfileDto? lockedProfile,
        Action onContinue,
        Action onBack)
    {
        _profilesQuery = profilesQuery;
        Mode = mode;
        _onContinue = onContinue;
        _onBack = onBack;

        if (mode == RitualMode.Planned && lockedProfile is not null)
        {
            LockedProfileCard = new RenProfileCardViewModel(lockedProfile);
            IsLoading = false;
        }
        else
        {
            _ = LoadProfilesAsync();
        }
    }

    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        try
        {
            if (_profilesQuery is null) return;
            var profiles = await _profilesQuery.HandleAsync(new GetAllBlockProfilesQuery { ProfileType = BlockType.Focus });
            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(new RenProfileCardViewModel(p));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectProfile(RenProfileCardViewModel card)
    {
        if (SelectedProfileCard == card)
        {
            card.IsSelected = false;
            SelectedProfileCard = null;
        }
        else
        {
            if (SelectedProfileCard is not null)
                SelectedProfileCard.IsSelected = false;
            card.IsSelected = true;
            SelectedProfileCard = card;
        }
    }

    [RelayCommand]
    private void ToggleLockedProfileExpanded()
    {
        if (LockedProfileCard is not null)
            LockedProfileCard.IsExpanded = !LockedProfileCard.IsExpanded;
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

        // Duplicate check against the locked profile's existing sites.
        if (LockedProfileCard is not null)
        {
            foreach (var s in LockedProfileCard.Profile.Websites)
            {
                if (string.Equals(s, raw, StringComparison.OrdinalIgnoreCase))
                {
                    AddSiteError = $"\"{raw}\" is already in this profile.";
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
        if (string.IsNullOrWhiteSpace(exePath)) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var a in AdditionalBlockedApps)
        {
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return;
        }

        if (LockedProfileCard is not null)
        {
            foreach (var a in LockedProfileCard.Profile.BlockedApps)
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
        if (!IsPlannedMode && SelectedProfileCard is not null)
        {
            SelectedProfileCard.IsSelected = false;
            SelectedProfileCard = null;
        }
        _onBack();
    }
}
