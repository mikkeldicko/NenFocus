using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SaveReflection;

namespace NenFocus.ViewModels;

public partial class PostSessionViewModel : ViewModelBase
{
    private readonly ICommandHandler<SaveReflectionCommand, SaveReflectionResult> _saveReflection;
    private readonly Action _onDone;
    private readonly PostSessionDto _data;

    // ── Display properties ──────────────────────────────────────────────────

    public string Goal { get; }
    public string ProfileName { get; }
    public DateTime StartedAt { get; }
    public DateTime EndedAt { get; }
    public TimeSpan Duration { get; }
    public TimeSpan PlannedDuration { get; }
    public int BreaksTaken { get; }
    public bool HasBreaks { get; }
    public int TotalBlockedAttempts { get; }
    public bool HasBlockedAttempts { get; }

    public ObservableCollection<BlockedAttemptItemViewModel> BlockedAttemptItems { get; } = new();

    // ── Reflection ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _reflectionText = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isSaved;

    public PostSessionViewModel(
        PostSessionDto data,
        ICommandHandler<SaveReflectionCommand, SaveReflectionResult> saveReflection,
        Action onDone)
    {
        _data = data;
        _saveReflection = saveReflection;
        _onDone = onDone;

        Goal = data.Goal;
        ProfileName = data.ProfileName;
        StartedAt = data.StartedAt.ToLocalTime();
        EndedAt = data.EndedAt.ToLocalTime();
        Duration = data.ActualDuration;
        PlannedDuration = data.PlannedDuration;
        BreaksTaken = data.BreaksTaken;
        HasBreaks = data.BreaksTaken > 0;
        TotalBlockedAttempts = data.TotalBlockedAttempts;
        HasBlockedAttempts = data.TotalBlockedAttempts > 0;

        foreach (var kvp in data.BlockedAppAttempts.OrderByDescending(x => x.Value))
            BlockedAttemptItems.Add(new BlockedAttemptItemViewModel(kvp.Key, kvp.Value, "App"));

        foreach (var kvp in data.BlockedWebsiteAttempts.OrderByDescending(x => x.Value))
            BlockedAttemptItems.Add(new BlockedAttemptItemViewModel(kvp.Key, kvp.Value, "Site"));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Done()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ReflectionText) && !IsSaved)
            {
                IsSaving = true;
                var result = await _saveReflection.HandleAsync(new SaveReflectionCommand
                {
                    SessionId = _data.SessionId,
                    Reflection = ReflectionText
                });

                if (result.Success)
                {
                    IsSaved = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Save failed: {result.ErrorMessage}");
                    return;
                }
            }

            _onDone();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Critical error: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }
}

/// <summary>Display item for a blocked app or website attempt in the post-session list.</summary>
public sealed class BlockedAttemptItemViewModel
{
    public string ProcessName { get; }
    public int AttemptCount { get; }
    public string Display { get; }
    public string TypeLabel { get; }

    public BlockedAttemptItemViewModel(string name, int count, string type)
    {
        ProcessName = name;
        AttemptCount = count;
        TypeLabel = type;
        Display = $"{name} ({type})";
    }
}
