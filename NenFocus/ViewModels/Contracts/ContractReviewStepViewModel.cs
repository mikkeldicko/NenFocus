using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.ViewModels.Components;

namespace NenFocus.ViewModels.Contracts;

/// <summary>
/// Terminal step: read-only summary of everything collected, mirroring HatsuPageViewModel's
/// role as the step that owns the actual use-case dispatch (here: optionally saving a Contract
/// Preset) before handing assembled data back up via a callback.
/// </summary>
public partial class ContractReviewStepViewModel : ViewModelBase
{
    private readonly ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> _createPreset;
    private readonly Func<NenContractDraftRules> _getData;
    private readonly CreateContractPresetCommand? _presetToSave;
    private readonly Action<NenContractDraftRules> _onReadyToSchedule;
    private readonly Action _onBack;

    public string SummaryName { get; }
    public string SummaryDateRange { get; }
    public string SummaryMode { get; }
    public string SummaryStrictness { get; }
    public string SummaryFloor { get; }
    public string SummaryWorkHours { get; }
    public string SummaryRestDays { get; }
    public string SummaryCancellation { get; }

    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _error;

    public ContractReviewStepViewModel(
        ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createPreset,
        Func<NenContractDraftRules> getData,
        CreateContractPresetCommand? presetToSave,
        string summaryName,
        string summaryDateRange,
        string summaryMode,
        string summaryStrictness,
        string summaryFloor,
        string summaryWorkHours,
        string summaryRestDays,
        string summaryCancellation,
        Action<NenContractDraftRules> onReadyToSchedule,
        Action onBack)
    {
        _createPreset = createPreset;
        _getData = getData;
        _presetToSave = presetToSave;
        SummaryName = summaryName;
        SummaryDateRange = summaryDateRange;
        SummaryMode = summaryMode;
        SummaryStrictness = summaryStrictness;
        SummaryFloor = summaryFloor;
        SummaryWorkHours = summaryWorkHours;
        SummaryRestDays = summaryRestDays;
        SummaryCancellation = summaryCancellation;
        _onReadyToSchedule = onReadyToSchedule;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task BeginSchedulingAsync()
    {
        Error = null;
        IsSaving = true;
        try
        {
            if (_presetToSave is not null)
            {
                var result = await _createPreset.HandleAsync(_presetToSave);
                if (!result.Success)
                {
                    Error = $"Could not save preset: {result.ErrorMessage}";
                    return;
                }
            }

            _onReadyToSchedule(_getData());
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand] private void Back() => _onBack();
}
