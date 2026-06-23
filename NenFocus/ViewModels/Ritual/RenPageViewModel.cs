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

namespace NenFocus.ViewModels.Ritual;

/// <summary>Wraps a BlockProfileDto with selection state for the Ren page profile list.</summary>
public partial class RenProfileCardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    public BlockProfileDto Profile { get; }

    public RenProfileCardViewModel(BlockProfileDto profile)
    {
        Profile = profile;
    }
}

public partial class RenPageViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _profilesQuery;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    // ── Profile list ───────────────────────────────────────────────────────
    public ObservableCollection<RenProfileCardViewModel> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfile))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(IsAllowlistMode))]
    [NotifyPropertyChangedFor(nameof(CanAddWebsites))]
    [NotifyPropertyChangedFor(nameof(AllowlistHintVisible))]
    private RenProfileCardViewModel? _selectedProfileCard;

    public BlockProfileDto? SelectedProfile => SelectedProfileCard?.Profile;
    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool IsAllowlistMode => SelectedProfile?.WebsitePolicy == WebsitePolicy.Allowlist;

    /// <summary>Websites can be added in any mode except allowlist with a profile selected.</summary>
    public bool CanAddWebsites => !IsAllowlistMode;
    public bool AllowlistHintVisible => IsAllowlistMode;

    [ObservableProperty]
    private bool _isLoading = true;

    // ── Session-only additions ─────────────────────────────────────────────
    public ObservableCollection<string> SessionOnlyApps { get; } = new();
    public ObservableCollection<string> SessionOnlyWebsites { get; } = new();

    [ObservableProperty]
    private string _newAppInput = string.Empty;

    [ObservableProperty]
    private string _newWebsiteInput = string.Empty;

    public RenPageViewModel(
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> profilesQuery,
        Action onContinue,
        Action onBack)
    {
        _profilesQuery = profilesQuery;
        _onContinue = onContinue;
        _onBack = onBack;
        _ = LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        try
        {
            var profiles = await _profilesQuery.HandleAsync(new GetAllBlockProfilesQuery());
            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(new RenProfileCardViewModel(p));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────

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
    private void AddApp()
    {
        var app = NewAppInput.Trim();
        if (!string.IsNullOrWhiteSpace(app) && !SessionOnlyApps.Contains(app))
        {
            SessionOnlyApps.Add(app);
            NewAppInput = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveApp(string app) => SessionOnlyApps.Remove(app);

    [RelayCommand]
    private void AddWebsite()
    {
        var site = NewWebsiteInput.Trim();
        if (!string.IsNullOrWhiteSpace(site) && !SessionOnlyWebsites.Contains(site))
        {
            SessionOnlyWebsites.Add(site);
            NewWebsiteInput = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveWebsite(string site) => SessionOnlyWebsites.Remove(site);

    [RelayCommand]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back()
    {
        SessionOnlyApps.Clear();
        SessionOnlyWebsites.Clear();
        NewAppInput = string.Empty;
        NewWebsiteInput = string.Empty;
        if (SelectedProfileCard is not null)
        {
            SelectedProfileCard.IsSelected = false;
            SelectedProfileCard = null;
        }
        _onBack();
    }
}

