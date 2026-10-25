using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.Interfaces;
using System;

namespace NenFocus.ViewModels.DevTools;

/// <summary>
/// Corner overlay that lets a developer jump the app's clock forward to see how
/// scheduled work reacts — a missed block, a broken contract, an ended session —
/// without waiting for real time to pass. Toggled by a global hotkey; hidden by
/// default so it never appears during ordinary use. All date math here is trivial
/// glue over already-typed picker values, not tactical logic — the real "what time
/// is it" authority stays in <see cref="IDebugClockController"/>.
/// </summary>
public partial class DebugClockViewModel : ViewModelBase
{
    private readonly ISystemClock _clock;
    private readonly IDebugClockController _controller;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private DateTime _currentEffectiveTime;
    [ObservableProperty] private DateTimeOffset _selectedDate;
    [ObservableProperty] private TimeSpan _selectedTime;
    // decimal?, not int — must match NumericUpDown.Value's own type exactly. Binding
    // straight to a non-nullable int crashed (NullReferenceException unboxing null)
    // the moment the control's value was ever null, which it legitimately can be
    // (a momentarily empty field, or before the control's own default first applies).
    [ObservableProperty] private decimal? _customMinutes = 5;

    public DebugClockViewModel(ISystemClock clock, IDebugClockController controller, IDisplayClock displayClock)
    {
        _clock = clock;
        _controller = controller;

        // The live second-hand; a jump raises it at once too (Gap 89 Slice A).
        displayClock.SecondTicked += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Refresh);

        Refresh();
    }

    private void Refresh()
    {
        CurrentEffectiveTime = _clock.TimeZone.ToCalendarTime(_clock.Now);
        IsOverridden = _controller.IsOverridden;
    }

    [RelayCommand]
    private void ToggleVisible()
    {
        if (!IsVisible)
        {
            var now = _clock.TimeZone.ToCalendarTime(_clock.Now);
            SelectedDate = new DateTimeOffset(now.Date);
            SelectedTime = now.TimeOfDay;
        }

        IsVisible = !IsVisible;
    }

    [RelayCommand]
    private void SetTime()
    {
        var local = SelectedDate.Date + SelectedTime;
        _controller.SetTo(DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime());
    }

    [RelayCommand]
    private void AdvanceCustomMinutes()
    {
        if (CustomMinutes is not { } minutes || minutes <= 0) return;
        _controller.AdvanceBy(TimeSpan.FromMinutes((double)minutes));
    }

    [RelayCommand]
    private void AdvanceOneHour() => _controller.AdvanceBy(TimeSpan.FromHours(1));

    [RelayCommand]
    private void AdvanceSixHours() => _controller.AdvanceBy(TimeSpan.FromHours(6));

    [RelayCommand]
    private void AdvanceOneDay() => _controller.AdvanceBy(TimeSpan.FromDays(1));

    [RelayCommand]
    private void Reset() => _controller.Reset();
}
