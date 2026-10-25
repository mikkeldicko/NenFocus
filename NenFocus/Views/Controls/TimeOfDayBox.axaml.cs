using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NenFocus.Converters;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Views.Controls;

/// <summary>One row of the dropdown: the time, its text, and — for an End field — its length.</summary>
public sealed record TimeOfDayOption(TimeSpan Value, string Text, TimeSpan? Length);

/// <summary>
/// A time-of-day field, Google-Calendar style: type it, or pick from a dropdown of choices that
/// follows what's typed. The ViewModel binds a time, never text (Gap 89 Slice B).
/// <list type="bullet">
/// <item><see cref="Value"/> changes only when the user is done — the field loses focus, Enter, or
/// a pick (a click, or Up/Down) — so a ViewModel never sees half-typed text like "10:3". Text that
/// isn't a time leaves it null, and the field shows a red border until it's edited again.</item>
/// <item><see cref="Options"/> are the dropdown's choices (quarter hours, 00:00–24:00, unless set).</item>
/// <item><see cref="DurationFrom"/>, set on an End field, labels each choice with its length from that
/// time and orders typing and arrow keys by length, so an End past midnight sorts after a shorter one.</item>
/// </list>
/// Text ↔ time is <see cref="TimeOfDayToStringConverter"/>'s; nothing here is a business rule.
/// </summary>
public partial class TimeOfDayBox : UserControl
{
    public static readonly StyledProperty<TimeSpan?> ValueProperty =
        AvaloniaProperty.Register<TimeOfDayBox, TimeSpan?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<TimeSpan>> OptionsProperty =
        AvaloniaProperty.Register<TimeOfDayBox, IReadOnlyList<TimeSpan>>(nameof(Options), QuarterHours());

    public static readonly StyledProperty<TimeSpan?> DurationFromProperty =
        AvaloniaProperty.Register<TimeOfDayBox, TimeSpan?>(nameof(DurationFrom));

    public TimeSpan? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IReadOnlyList<TimeSpan> Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public TimeSpan? DurationFrom
    {
        get => GetValue(DurationFromProperty);
        set => SetValue(DurationFromProperty, value);
    }

    private const int VisibleRows = 5;

    private readonly TextBox _box;
    private readonly Border _frame;
    private readonly Popup _dropDown;
    private readonly ScrollViewer _scroller;
    private readonly ItemsControl _list;

    private IReadOnlyList<TimeOfDayOption> _items = [];
    private int? _highlightedIndex;
    private Border? _highlighted;
    private bool _writingText;      // the text is being set from Value, not typed
    private bool _committing;       // Value is being set from the text, not from outside
    private bool _invalid;

    public TimeOfDayBox()
    {
        InitializeComponent();
        _box = this.FindControl<TextBox>("Box")!;
        _frame = this.FindControl<Border>("Frame")!;
        _dropDown = this.FindControl<Popup>("DropDown")!;
        _scroller = this.FindControl<ScrollViewer>("Scroller")!;
        _list = this.FindControl<ItemsControl>("List")!;

        RebuildItems();

        _box.GotFocus += (_, _) => Open();
        _box.AddHandler(PointerPressedEvent, (_, _) => Open(), RoutingStrategies.Bubble, handledEventsToo: true);
        _box.LostFocus += (_, _) => CommitTypedText();
        _box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !_writingText) OnTyped();
        };

        // Tunnel: a TextBox with a selection handles Down itself before a bubbling handler sees it (Gap 102).
        _box.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        _list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (OptionUnder(e.Source) is { } index) { Pick(index); Close(); }
        }, RoutingStrategies.Bubble);
        // PointerMoved, not PointerEntered: the latter is Direct-routed and never reaches the list.
        _list.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (OptionUnder(e.Source) is { } index && index != _highlightedIndex) Highlight(index);
        }, RoutingStrategies.Bubble);

        // Closing the dropdown also ends the edit — an outside click otherwise leaves the text
        // selected and still taking keystrokes.
        _dropDown.Closed += (_, _) =>
        {
            if (_box.IsFocused) TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && !_committing)
        {
            // Set from outside: show it. A null from outside clears the field.
            WriteText(Value is { } time ? TimeOfDayToStringConverter.Format(time) : string.Empty);
            SetInvalid(false);
            if (_dropDown.IsOpen) CenterOn(ApproxTyped());
        }
        else if (change.Property == OptionsProperty || change.Property == DurationFromProperty)
        {
            RebuildItems();
        }
    }

    private void Open()
    {
        _box.SelectAll();
        SetInvalid(false);
        _dropDown.IsOpen = true;
        CenterOn(ApproxTyped());
    }

    private void Close() => _dropDown.IsOpen = false;

    private void OnTyped()
    {
        if (!_box.IsFocused) return;
        SetInvalid(false);
        CenterOn(ApproxTyped());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Step(TimeOptionLookup.IndexAfter);
                e.Handled = true;
                break;
            case Key.Up:
                Step(TimeOptionLookup.IndexBefore);
                e.Handled = true;
                break;
            case Key.Enter:
                // A whole time is kept as typed; a partial one ("17") takes the highlighted row.
                if (TimeOfDayToStringConverter.TryParse(_box.Text ?? string.Empty, out _)) CommitTypedText();
                else if (_highlightedIndex is { } index) Pick(index);
                else CommitTypedText();
                Close();
                break;
            case Key.Escape:
                Close();
                break;
        }
    }

    /// <summary>Up/Down: the next row from what's typed (off-grid 14:07 → 14:15), committed as a pick.</summary>
    private void Step(Func<TimeSpan, IReadOnlyList<TimeSpan>, int> next)
    {
        if (_items.Count == 0) return;
        var from = ApproxTyped() is { } typed ? KeyOf(typed) : TimeSpan.Zero;
        Pick(next(from, Keys()));
    }

    private void Pick(int index)
    {
        var option = _items[index];
        WriteText(option.Text);
        SetValueFromText(option.Value);
        CenterOn(option.Value);
    }

    private void CommitTypedText()
    {
        var text = _box.Text ?? string.Empty;
        if (TimeOfDayToStringConverter.TryParse(text, out var time))
        {
            SetValueFromText(time);
            WriteText(TimeOfDayToStringConverter.Format(time));
            SetInvalid(false);
        }
        else
        {
            SetValueFromText(null);
            SetInvalid(true);
        }
    }

    private void SetValueFromText(TimeSpan? time)
    {
        _committing = true;
        try { SetCurrentValue(ValueProperty, time); }
        finally { _committing = false; }
    }

    private void WriteText(string text)
    {
        _writingText = true;
        try { _box.Text = text; }
        finally { _writingText = false; }
    }

    private void SetInvalid(bool invalid)
    {
        _invalid = invalid;
        _frame.Classes.Set("invalid", _invalid);
    }

    /// <summary>The time the typing points at: an exact time, else a guessed hour ("14" → 14:00), else none.</summary>
    private TimeSpan? ApproxTyped()
    {
        var text = _box.Text ?? string.Empty;
        if (TimeOfDayToStringConverter.TryParse(text, out var exact)) return exact;
        if (TimeOptionLookup.TryGuessHour(text, out var guess)) return guess;
        return null;
    }

    /// <summary>Where a time sorts: by length from DurationFrom on an End field, else by the clock.</summary>
    private TimeSpan KeyOf(TimeSpan time) =>
        DurationFrom is { } from ? PlannerTimeShiftMath.ResolvedDuration(from, time) : time;

    private IReadOnlyList<TimeSpan> Keys() => _items.Select(o => KeyOf(o.Value)).ToList();

    private void RebuildItems()
    {
        _items = Options
            .Select(t => new TimeOfDayOption(t, TimeOfDayToStringConverter.Format(t),
                DurationFrom is { } from ? PlannerTimeShiftMath.ResolvedDuration(from, t) : null))
            .ToList();
        _list.ItemsSource = _items;
        _highlighted = null;
        _highlightedIndex = null;
        if (_dropDown.IsOpen) CenterOn(ApproxTyped());
    }

    private int? OptionUnder(object? source) =>
        source is StyledElement { DataContext: TimeOfDayOption option } ? IndexOf(option) : null;

    private int? IndexOf(TimeOfDayOption option)
    {
        for (var i = 0; i < _items.Count; i++)
            if (ReferenceEquals(_items[i], option)) return i;
        return null;
    }

    private void CenterOn(TimeSpan? time)
    {
        if (time is null || _items.Count == 0) return;
        var index = TimeOptionLookup.NearestIndex(KeyOf(time.Value), Keys());
        ScrollTo(index);
    }

    /// <summary>
    /// Scrolls so <paramref name="index"/> sits in a five-row window and highlights it. Posted at
    /// Loaded priority and retried: the popup's rows have no height until it has laid out once.
    /// </summary>
    private void ScrollTo(int index, int attemptsLeft = 5)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_dropDown.IsOpen || index >= _items.Count) return;
            if (_scroller.Extent.Height <= 0)
            {
                if (attemptsLeft > 0) ScrollTo(index, attemptsLeft - 1);
                return;
            }

            var (start, _) = DropdownWindow.Bounds(index, _items.Count, VisibleRows);
            var rowHeight = _scroller.Extent.Height / _items.Count;
            _scroller.MaxHeight = VisibleRows * rowHeight;
            _scroller.Offset = new Vector(_scroller.Offset.X, start * rowHeight);
            Highlight(index);
        }, DispatcherPriority.Loaded);
    }

    private void Highlight(int index)
    {
        _highlighted?.Classes.Remove("highlighted");
        _highlightedIndex = index;
        _highlighted = _list.ContainerFromIndex(index)?.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        _highlighted?.Classes.Add("highlighted");
    }

    private static IReadOnlyList<TimeSpan> QuarterHours() =>
        Enumerable.Range(0, 24 * 4 + 1).Select(i => TimeSpan.FromMinutes(15 * i)).ToList();
}
