using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Planner.ContractWizard;

/// <summary>
/// Terminal step: read-only summary of everything collected, mirroring HatsuPageViewModel's
/// role as the step that owns the actual use-case dispatch before handing assembled data back
/// up via a callback. No longer saves a Contract Preset here — presets are now authored and
/// edited entirely on the Presets page (see ContractPresetPageViewModel); the Rules step just
/// picks one.
/// </summary>
public partial class ContractReviewStepViewModel : ViewModelBase
{
    private readonly Func<NenContractDraftRules> _getData;
    private readonly Action<NenContractDraftRules> _onReadyToSchedule;
    private readonly Action _onBack;

    public string SummaryName { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public string SummaryMode { get; }
    public string SummaryStrictness { get; }
    public double? DailyFloorHours { get; }
    public TimeOnly? WorkHoursStart { get; }
    public TimeOnly? WorkHoursEnd { get; }
    public IReadOnlyList<DateOnly> RestDays { get; }
    public string SummaryRedemptionPlacement { get; }

    public ContractReviewStepViewModel(
        Func<NenContractDraftRules> getData,
        string summaryName,
        DateOnly startDate,
        DateOnly endDate,
        string summaryMode,
        string summaryStrictness,
        double? dailyFloorHours,
        TimeOnly? workHoursStart,
        TimeOnly? workHoursEnd,
        IReadOnlyList<DateOnly> restDays,
        string summaryRedemptionPlacement,
        Action<NenContractDraftRules> onReadyToSchedule,
        Action onBack)
    {
        _getData = getData;
        SummaryName = summaryName;
        StartDate = startDate;
        EndDate = endDate;
        SummaryMode = summaryMode;
        SummaryStrictness = summaryStrictness;
        DailyFloorHours = dailyFloorHours;
        WorkHoursStart = workHoursStart;
        WorkHoursEnd = workHoursEnd;
        RestDays = restDays;
        SummaryRedemptionPlacement = summaryRedemptionPlacement;
        _onReadyToSchedule = onReadyToSchedule;
        _onBack = onBack;
    }

    [RelayCommand]
    private void BeginScheduling() => _onReadyToSchedule(_getData());

    [RelayCommand] private void Back() => _onBack();
}
