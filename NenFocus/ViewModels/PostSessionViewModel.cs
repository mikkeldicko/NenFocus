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
    public string StartedAtDisplay { get; }
    public string EndedAtDisplay { get; }
    public string DurationDisplay { get; }
    public string PlannedDurationDisplay { get; }
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
        StartedAtDisplay = data.StartedAt.ToLocalTime().ToString("h:mm tt");
        EndedAtDisplay = data.EndedAt.ToLocalTime().ToString("h:mm tt");
        BreaksTaken = data.BreaksTaken;
        HasBreaks = data.BreaksTaken > 0;
        TotalBlockedAttempts = data.TotalBlockedAttempts;
        HasBlockedAttempts = data.TotalBlockedAttempts > 0;

        // Duration display
        var actual = data.EndedAt - data.StartedAt;
        DurationDisplay = FormatDuration(actual);
        PlannedDurationDisplay = FormatDuration(TimeSpan.FromMinutes(data.PlannedDurationMinutes));

        // Fix for Apps
        foreach (var kvp in data.BlockedAppAttempts.OrderByDescending(x => x.Value))
        {
            // Added "App" as the third argument
            BlockedAttemptItems.Add(new BlockedAttemptItemViewModel(kvp.Key, kvp.Value, "App"));
        }

        // Fix for Websites
        foreach (var kvp in data.BlockedWebsiteAttempts.OrderByDescending(x => x.Value))
        {
            // Added "Site" as the third argument
            BlockedAttemptItems.Add(new BlockedAttemptItemViewModel(kvp.Key, kvp.Value, "Site"));
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalMinutes}m";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Done()
    {
        try
        {
            // Only attempt to save if there is text and we haven't saved successfully yet
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
                    IsSaved = true; // Mark as saved so we don't try again
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Save failed: {result.ErrorMessage}");
                    return; // Stop here so the user can try again
                }
            }

            // If we get here, either we saved successfully OR the user left the text empty.
            // In both cases, we are finished with this view.
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

    /// <summary>Display item for a blocked app attempt in the post-session list.</summary>
    public sealed class BlockedAttemptItemViewModel
    {
        public string ProcessName { get; }
        public int AttemptCount { get; }
        public string Display { get; }
        public string TypeLabel { get; } // "App" or "Site"

    public BlockedAttemptItemViewModel(string name, int count, string type)
    {
        ProcessName = name;
        AttemptCount = count;
        TypeLabel = type;
        Display = $"{name} ({type})";
    }
}

