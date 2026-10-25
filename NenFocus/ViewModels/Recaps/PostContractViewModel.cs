using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;
using NenFocus.Application.UseCases.NenContracts.SaveReflection;
using NenFocus.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Recaps;

public partial class PostContractViewModel : ViewModelBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly Action _onDone;
    private readonly NenContractRecapDto _data;

    public string Name { get; }
    public string HeaderLabel { get; }
    public string ReflectionPrompt { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public double CommittedFocusHours { get; }
    public double TotalCompletedHours { get; }
    public double TotalBonusHours { get; }
    public bool HasBonusHours { get; }
    public string? CompletionGrade { get; }
    public bool HasGrade { get; }
    public int BestStreakDays { get; }
    public bool HasStreak { get; }

    public int TotalFocusBlocks { get; }
    public int TotalBackgroundBlocks { get; }
    public int RedemptionBlocks { get; }
    public int CompletedOnTimeBlocks { get; }
    public int StartedLateBlocks { get; }
    public int EndedEarlyBlocks { get; }
    public int MissedBlocks { get; }
    public int CancelledBlocks { get; }
    public int BonusBlockCount { get; }

    public ObservableCollection<DailyRecapItemViewModel> DailyBreakdownItems { get; } = new();
    public bool HasDailyBreakdown { get; }

    [ObservableProperty]
    private string _reflectionText = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private string? _errorMessage;

    public PostContractViewModel(
        NenContractRecapDto data,
        ICommandDispatcher commandDispatcher,
        Action onDone)
    {
        _data = data;
        _commandDispatcher = commandDispatcher;
        _onDone = onDone;

        Name = data.Name;
        HeaderLabel = data.Status == NenContractStatus.Broken ? "CONTRACT BROKEN" : "CONTRACT COMPLETE";
        ReflectionPrompt = data.Status == NenContractStatus.Broken
            ? "What led you to break this contract?"
            : "How did this contract go? What did you accomplish?";
        StartDate = data.StartDate;
        EndDate = data.EndDate;
        CommittedFocusHours = data.CommittedFocusHours;
        TotalCompletedHours = data.TotalCompletedHours;
        TotalBonusHours = data.TotalBonusHours;
        HasBonusHours = data.TotalBonusHours > 0;
        CompletionGrade = data.CompletionGrade;
        HasGrade = data.CompletionGrade is not null;
        BestStreakDays = data.BestStreakDays;
        HasStreak = data.BestStreakDays > 0;

        TotalFocusBlocks = data.TotalFocusBlocks;
        TotalBackgroundBlocks = data.TotalBackgroundBlocks;
        RedemptionBlocks = data.RedemptionBlocks;
        CompletedOnTimeBlocks = data.CompletedOnTimeBlocks;
        StartedLateBlocks = data.StartedLateBlocks;
        EndedEarlyBlocks = data.EndedEarlyBlocks;
        MissedBlocks = data.MissedBlocks;
        CancelledBlocks = data.CancelledBlocks;
        BonusBlockCount = data.BonusBlockCount;

        foreach (var day in data.DailyBreakdown.OrderBy(d => d.Date))
            DailyBreakdownItems.Add(new DailyRecapItemViewModel(day.Date, day.Percentage));
        HasDailyBreakdown = DailyBreakdownItems.Count > 0;
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
                var result = await _commandDispatcher.DispatchAsync(new SaveNenContractReflectionCommand
                {
                    ContractId = _data.ContractId,
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

            await _commandDispatcher.DispatchAsync(new AcknowledgePostContractCommand
            {
                ContractId = _data.ContractId
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

public sealed class DailyRecapItemViewModel
{
    public DateOnly Date { get; }

    /// <summary>Null means rest day — the View's PercentageToLabelConverter renders that as
    /// "Rest" rather than the ViewModel deciding the display text itself.</summary>
    public double? Percentage { get; }

    public DailyRecapItemViewModel(DateOnly date, double? percentage)
    {
        Date = date;
        Percentage = percentage;
    }
}
