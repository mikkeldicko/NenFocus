using System;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Recaps;

public partial class MissedSessionsTriageViewModel : ViewModelBase
{
    private readonly Action _onSkipAll;
    private readonly Action _onReviewEach;

    public MissedSessionsTriageViewModel(
        int count,
        Action onSkipAll,
        Action onReviewEach)
    {
        Count = count;
        _onSkipAll = onSkipAll;
        _onReviewEach = onReviewEach;
    }

    public int Count { get; }

    [RelayCommand]
    private void SkipAll() => _onSkipAll();

    [RelayCommand]
    private void ReviewEach() => _onReviewEach();
}
