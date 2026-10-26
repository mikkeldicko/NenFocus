using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Recaps;

public partial class PostSessionViewModel : ViewModelBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly Action _onDone;
    private readonly PostSessionDto _data;


    public string Goal { get; }
    public string HeaderLabel { get; }
    public string ReflectionPrompt { get; }
    public string PresetName { get; }
    public DateTime StartedAt { get; }
    public DateTime EndedAt { get; }
    public TimeSpan Duration { get; }
    public TimeSpan PlannedDuration { get; }
    public int BreaksTaken { get; }
    public bool HasBreaks { get; }
    public int TotalBlockedAttempts { get; }
    public bool HasBlockedAttempts { get; }

    public ObservableCollection<BlockedAttemptItemViewModel> BlockedAttemptItems { get; } = new();


    [ObservableProperty]
    private string _reflectionText = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private string? _errorMessage;

    public PostSessionViewModel(
        PostSessionDto data,
        ICommandDispatcher commandDispatcher,
        Action onDone,
        UserTimeZone timeZone)
    {
        _data = data;
        _commandDispatcher = commandDispatcher;
        _onDone = onDone;

        Goal = data.Goal;
        HeaderLabel = data.Status == SessionStatus.Missed ? "SESSION MISSED" : "SESSION COMPLETE";
        ReflectionPrompt = data.Status == SessionStatus.Missed
            ? "What got in the way?"
            : "How did the session go? What did you accomplish?";
        PresetName = data.PresetName;
        (StartedAt, EndedAt) = timeZone.ToCalendarTimes(data.Recorded);
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
    private async Task Done()
    {
        try
        {
            ErrorMessage = null;

            if (!string.IsNullOrWhiteSpace(ReflectionText) && !IsSaved)
            {
                IsSaving = true;
                var result = await _commandDispatcher.DispatchAsync(new SaveReflectionCommand
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
                    ErrorMessage = result.ErrorMessage;
                    return;
                }
            }

            await _commandDispatcher.DispatchAsync(new AcknowledgePostSessionCommand
            {
                SessionId = _data.SessionId
            });

            _onDone();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }
}

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

