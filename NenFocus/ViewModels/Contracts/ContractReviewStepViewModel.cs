using System;
using CommunityToolkit.Mvvm.Input;
using NenFocus.ViewModels.Components;

namespace NenFocus.ViewModels.Contracts;

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
    public string SummaryDateRange { get; }
    public string SummaryMode { get; }
    public string SummaryStrictness { get; }
    public string SummaryFloor { get; }
    public string SummaryWorkHours { get; }
    public string SummaryRestDays { get; }

    public ContractReviewStepViewModel(
        Func<NenContractDraftRules> getData,
        string summaryName,
        string summaryDateRange,
        string summaryMode,
        string summaryStrictness,
        string summaryFloor,
        string summaryWorkHours,
        string summaryRestDays,
        Action<NenContractDraftRules> onReadyToSchedule,
        Action onBack)
    {
        _getData = getData;
        SummaryName = summaryName;
        SummaryDateRange = summaryDateRange;
        SummaryMode = summaryMode;
        SummaryStrictness = summaryStrictness;
        SummaryFloor = summaryFloor;
        SummaryWorkHours = summaryWorkHours;
        SummaryRestDays = summaryRestDays;
        _onReadyToSchedule = onReadyToSchedule;
        _onBack = onBack;
    }

    [RelayCommand]
    private void BeginScheduling() => _onReadyToSchedule(_getData());

    [RelayCommand] private void Back() => _onBack();
}
