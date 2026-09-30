using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Pages;
using NenFocus.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace NenFocus.Views;

public partial class PlannerPageView : UserControl
{
    // ── Column references ─────────────────────────────────────────────────────
    private Grid[] _dayColumns = Array.Empty<Grid>();
    private PlannerPageViewModel? _vm;

    // ── Drag state (cleared after each gesture) ───────────────────────────────
    private enum DragKind { None, CreatePending, Creating, MovePending, Moving, ResizingTopPending, ResizingTop, ResizingBottomPending, ResizingBottom }

    private DragKind _dragKind = DragKind.None;
    private PlannedBlockTileViewModel? _dragTile;
    private Button? _dragButton;
    private Canvas? _dragCanvas;
    private Point _dragStartPoint;       // pointer position at press, relative to canvas
    private double _tileOriginalTop;
    private double _tileOriginalHeight;
    private int _effectiveDragColumn;    // column the pointer is currently over (changes on cross-day drag)

    private const double DragThresholdPx = 4.0;
    private const double ResizeHotZonePx = 10.0;

    private bool _scrolledToCurrentTime; // set true after initial scroll; resets on view recreation

    // ── Ghost: drag-create visual (persists until form closes or RebuildTiles) ──
    private Border? _createGhost;
    private Canvas? _createGhostCanvas;
    private TextBlock? _ghostTimeLabel;

    // ── Ghost: tile move/resize overlay ──────────────────────────────────────
    private Border? _tileGhost;
    private Canvas? _tileGhostCanvas;
    private TextBlock? _tileGhostTimeLabel;

    // Wired once in the constructor (not AttachedToVisualTree, which can fire more than
    // once for the same control instance) since all these fields are part of the static
    // form/dialog UI, never rebuilt like the day columns are. Kept as fields so
    // OnViewModelPropertyChanged (a live user edit re-scrolling the open dropdown) can
    // reach them too, not just the constructor's own GotFocus wiring.
    //
    // All six time fields are plain TextBox + Popup/ScrollViewer/ItemsControl that the
    // ViewModel/this code-behind fully own now, not AutoCompleteBox — see
    // PlannerPageViewModel's StartTimeScrollIndex doc comment for why.
    private TextBox? _formStartTimeBox;
    private ScrollViewer? _formStartTimeScrollViewer;
    private ItemsControl? _formStartTimeList;
    private Border? _formStartTimeHighlighted;
    private int? _formStartTimeHoveredIndex;
    private TextBox? _formEndTimeBox;
    private ScrollViewer? _formEndTimeScrollViewer;
    private ItemsControl? _formEndTimeList;
    private Border? _formEndTimeHighlighted;
    private int? _formEndTimeHoveredIndex;
    private TextBox? _splitFirstStartTimeBox;
    private ScrollViewer? _splitFirstStartTimeScrollViewer;
    private ItemsControl? _splitFirstStartTimeList;
    private Border? _splitFirstStartTimeHighlighted;
    private int? _splitFirstStartTimeHoveredIndex;
    private TextBox? _splitFirstEndTimeBox;
    private ScrollViewer? _splitFirstEndTimeScrollViewer;
    private ItemsControl? _splitFirstEndTimeList;
    private Border? _splitFirstEndTimeHighlighted;
    private int? _splitFirstEndTimeHoveredIndex;
    private TextBox? _splitSecondStartTimeBox;
    private ScrollViewer? _splitSecondStartTimeScrollViewer;
    private ItemsControl? _splitSecondStartTimeList;
    private Border? _splitSecondStartTimeHighlighted;
    private int? _splitSecondStartTimeHoveredIndex;
    private TextBox? _splitSecondEndTimeBox;
    private ScrollViewer? _splitSecondEndTimeScrollViewer;
    private ItemsControl? _splitSecondEndTimeList;
    private Border? _splitSecondEndTimeHighlighted;
    private int? _splitSecondEndTimeHoveredIndex;

    public PlannerPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;

        _formStartTimeBox = this.FindControl<TextBox>("FormStartTimeBox")!;
        _formStartTimeScrollViewer = this.FindControl<ScrollViewer>("FormStartTimeScrollViewer")!;
        _formStartTimeList = this.FindControl<ItemsControl>("FormStartTimeList")!;
        _formEndTimeBox = this.FindControl<TextBox>("FormEndTimeBox")!;
        _formEndTimeScrollViewer = this.FindControl<ScrollViewer>("FormEndTimeScrollViewer")!;
        _formEndTimeList = this.FindControl<ItemsControl>("FormEndTimeList")!;
        _splitFirstStartTimeBox = this.FindControl<TextBox>("SplitFirstStartTimeBox")!;
        _splitFirstStartTimeScrollViewer = this.FindControl<ScrollViewer>("SplitFirstStartTimeScrollViewer")!;
        _splitFirstStartTimeList = this.FindControl<ItemsControl>("SplitFirstStartTimeList")!;
        _splitFirstEndTimeBox = this.FindControl<TextBox>("SplitFirstEndTimeBox")!;
        _splitFirstEndTimeScrollViewer = this.FindControl<ScrollViewer>("SplitFirstEndTimeScrollViewer")!;
        _splitFirstEndTimeList = this.FindControl<ItemsControl>("SplitFirstEndTimeList")!;
        _splitSecondStartTimeBox = this.FindControl<TextBox>("SplitSecondStartTimeBox")!;
        _splitSecondStartTimeScrollViewer = this.FindControl<ScrollViewer>("SplitSecondStartTimeScrollViewer")!;
        _splitSecondStartTimeList = this.FindControl<ItemsControl>("SplitSecondStartTimeList")!;
        _splitSecondEndTimeBox = this.FindControl<TextBox>("SplitSecondEndTimeBox")!;
        _splitSecondEndTimeScrollViewer = this.FindControl<ScrollViewer>("SplitSecondEndTimeScrollViewer")!;
        _splitSecondEndTimeList = this.FindControl<ItemsControl>("SplitSecondEndTimeList")!;

        // Nothing in the main form's two boxes has focus-driven behavior otherwise —
        // LostFocus isn't a Command binding target, so the code-behind wires it directly
        // into the ViewModel. The Split dialog's four boxes have no such snapback behavior.
        _formStartTimeBox.LostFocus += (_, _) => _vm?.OnFormStartTimeLostFocus();
        _formEndTimeBox.LostFocus += (_, _) => _vm?.OnFormEndTimeLostFocus();

        // Light-dismissing the popup (a genuine outside click) doesn't otherwise move
        // keyboard focus away from the TextBox on its own — confirmed via a real repro: the
        // text stayed selected/blue and still accepted keystrokes after clicking away. Popup
        // also closes this way when we programmatically call Close*TimeDropdown (item click,
        // Enter, Escape) — defocusing then too is a deliberate, low-risk choice: the field is
        // conceptually "done" either way once its dropdown closes. Focusing the page root
        // (Focusable="True" in XAML) rather than some other control is a neutral no-op target
        // with no styling of its own that would make the shift visible.
        this.FindControl<Popup>("FormStartTimePopup")!.Closed += (_, _) => Focus();
        this.FindControl<Popup>("FormEndTimePopup")!.Closed += (_, _) => Focus();
        this.FindControl<Popup>("SplitFirstStartTimePopup")!.Closed += (_, _) => Focus();
        this.FindControl<Popup>("SplitFirstEndTimePopup")!.Closed += (_, _) => Focus();
        this.FindControl<Popup>("SplitSecondStartTimePopup")!.Closed += (_, _) => Focus();
        this.FindControl<Popup>("SplitSecondEndTimePopup")!.Closed += (_, _) => Focus();

        // ScrollXTimeDropdownToWindow is called directly here rather than relying on
        // StartTimeScrollIndex/EndTimeScrollIndex's own PropertyChanged: [ObservableProperty]'s
        // generated setter skips raising the event when the new value equals the old one (e.g.
        // the form was just populated by SetFormTimes, which already computed this same
        // index) — a real repro where opening the dropdown right after creating a tile
        // silently didn't scroll at all, leaving whatever position a previous session
        // happened to leave the ScrollViewer at. Opening must always reposition, whether or
        // not the index number changed.
        WireTimePickerDropdown(_formStartTimeBox, _formStartTimeList,
            open: () => { _formStartTimeHoveredIndex = null; _vm?.OpenStartTimeDropdown(); ScrollStartTimeDropdownToWindow(_vm?.StartTimeScrollIndex); },
            moveNext: () => _vm?.MoveStartTimeNext(),
            movePrevious: () => _vm?.MoveStartTimePrevious(),
            close: () => _vm?.CloseStartTimeDropdown(),
            commit: () =>
            {
                var index = _formStartTimeHoveredIndex ?? _vm?.StartTimeScrollIndex;
                if (_vm is not null && index is { } i && i < _vm.TimeOptions.Count) _vm.SelectStartTimeOption(_vm.TimeOptions[i]);
                else _vm?.CloseStartTimeDropdown();
            },
            selectFromSource: source => { if (source.DataContext is string text) _vm?.SelectStartTimeOption(text); },
            hoverFromSource: source =>
            {
                if (_vm is null || _formStartTimeList is null || source.DataContext is not string text) return;
                var index = _vm.TimeOptions.IndexOf(text);
                if (index < 0 || index == _formStartTimeHoveredIndex) return;
                _formStartTimeHoveredIndex = index;
                SetHighlightedContainer(_formStartTimeList, () => _formStartTimeHighlighted, b => _formStartTimeHighlighted = b, index);
            });

        WireTimePickerDropdown(_formEndTimeBox, _formEndTimeList,
            open: () => { _formEndTimeHoveredIndex = null; _vm?.OpenEndTimeDropdown(); ScrollEndTimeDropdownToWindow(_vm?.EndTimeScrollIndex); },
            moveNext: () => _vm?.MoveEndTimeNext(),
            movePrevious: () => _vm?.MoveEndTimePrevious(),
            close: () => _vm?.CloseEndTimeDropdown(),
            commit: () =>
            {
                var index = _formEndTimeHoveredIndex ?? _vm?.EndTimeScrollIndex;
                if (_vm is not null && index is { } i && i < _vm.EndTimeOptions.Count) _vm.SelectEndTimeOption(_vm.EndTimeOptions[i].TimeText);
                else _vm?.CloseEndTimeDropdown();
            },
            selectFromSource: source => { if (source.DataContext is PlannerEndTimeOption dto) _vm?.SelectEndTimeOption(dto.TimeText); },
            hoverFromSource: source =>
            {
                if (_vm is null || _formEndTimeList is null || source.DataContext is not PlannerEndTimeOption dto) return;
                var index = _vm.EndTimeOptions.IndexOf(dto);
                if (index < 0 || index == _formEndTimeHoveredIndex) return;
                _formEndTimeHoveredIndex = index;
                SetHighlightedContainer(_formEndTimeList, () => _formEndTimeHighlighted, b => _formEndTimeHighlighted = b, index);
            });

        WireFlatTimePicker(_splitFirstStartTimeBox, _splitFirstStartTimeScrollViewer, _splitFirstStartTimeList,
            () => _splitFirstStartTimeHoveredIndex, v => _splitFirstStartTimeHoveredIndex = v,
            () => _splitFirstStartTimeHighlighted, b => _splitFirstStartTimeHighlighted = b,
            () => _vm?.IsSplitFirstStartTimeDropDownOpen == true, () => _vm?.OpenSplitFirstStartTimeDropdown(),
            () => _vm?.CloseSplitFirstStartTimeDropdown(), () => _vm?.MoveSplitFirstStartTimeNext(), () => _vm?.MoveSplitFirstStartTimePrevious(),
            text => _vm?.SelectSplitFirstStartTimeOption(text), () => _vm?.SplitFirstStartTimeScrollIndex);

        WireDurationTimePicker(_splitFirstEndTimeBox, _splitFirstEndTimeScrollViewer, _splitFirstEndTimeList,
            () => _splitFirstEndTimeHoveredIndex, v => _splitFirstEndTimeHoveredIndex = v,
            () => _splitFirstEndTimeHighlighted, b => _splitFirstEndTimeHighlighted = b,
            () => _vm?.IsSplitFirstEndTimeDropDownOpen == true, () => _vm?.OpenSplitFirstEndTimeDropdown(),
            () => _vm?.CloseSplitFirstEndTimeDropdown(), () => _vm?.MoveSplitFirstEndTimeNext(), () => _vm?.MoveSplitFirstEndTimePrevious(),
            text => _vm?.SelectSplitFirstEndTimeOption(text), () => _vm?.SplitFirstEndTimeScrollIndex, () => _vm?.SplitFirstEndTimeOptions);

        WireFlatTimePicker(_splitSecondStartTimeBox, _splitSecondStartTimeScrollViewer, _splitSecondStartTimeList,
            () => _splitSecondStartTimeHoveredIndex, v => _splitSecondStartTimeHoveredIndex = v,
            () => _splitSecondStartTimeHighlighted, b => _splitSecondStartTimeHighlighted = b,
            () => _vm?.IsSplitSecondStartTimeDropDownOpen == true, () => _vm?.OpenSplitSecondStartTimeDropdown(),
            () => _vm?.CloseSplitSecondStartTimeDropdown(), () => _vm?.MoveSplitSecondStartTimeNext(), () => _vm?.MoveSplitSecondStartTimePrevious(),
            text => _vm?.SelectSplitSecondStartTimeOption(text), () => _vm?.SplitSecondStartTimeScrollIndex);

        WireDurationTimePicker(_splitSecondEndTimeBox, _splitSecondEndTimeScrollViewer, _splitSecondEndTimeList,
            () => _splitSecondEndTimeHoveredIndex, v => _splitSecondEndTimeHoveredIndex = v,
            () => _splitSecondEndTimeHighlighted, b => _splitSecondEndTimeHighlighted = b,
            () => _vm?.IsSplitSecondEndTimeDropDownOpen == true, () => _vm?.OpenSplitSecondEndTimeDropdown(),
            () => _vm?.CloseSplitSecondEndTimeDropdown(), () => _vm?.MoveSplitSecondEndTimeNext(), () => _vm?.MoveSplitSecondEndTimePrevious(),
            text => _vm?.SelectSplitSecondEndTimeOption(text), () => _vm?.SplitSecondEndTimeScrollIndex, () => _vm?.SplitSecondEndTimeOptions);
    }

    /// <summary>
    /// Shared wiring for the four Split fields — all identical, plain-string-list pickers
    /// (same shape as Start's own, just parametrized instead of hand-duplicated four times).
    /// Delegates to WireTimePickerDropdown/ScrollTimeDropdownToWindow/SetHighlightedContainer,
    /// the same shared machinery Start/End use directly — this is only the extra plumbing
    /// specific to "plain flat list of strings", assembled once instead of four times over.
    /// </summary>
    private void WireFlatTimePicker(TextBox? box, ScrollViewer? scrollViewer, ItemsControl? list,
        Func<int?> getHoveredIndex, Action<int?> setHoveredIndex,
        Func<Border?> getHighlighted, Action<Border?> setHighlighted,
        Func<bool> isOpen, Action openViewModel, Action close, Action moveNext, Action movePrevious,
        Action<string> select, Func<int?> getScrollIndex)
    {
        void ScrollToWindow(int? centerIndex) => ScrollTimeDropdownToWindow(scrollViewer, list, isOpen, () => _vm?.TimeOptions.Count ?? 0, getHighlighted, setHighlighted, centerIndex);

        WireTimePickerDropdown(box, list,
            open: () => { setHoveredIndex(null); openViewModel(); ScrollToWindow(getScrollIndex()); },
            moveNext: moveNext,
            movePrevious: movePrevious,
            close: close,
            commit: () =>
            {
                var index = getHoveredIndex() ?? getScrollIndex();
                if (_vm is not null && index is { } i && i < _vm.TimeOptions.Count) select(_vm.TimeOptions[i]);
                else close();
            },
            selectFromSource: source => { if (source.DataContext is string text) select(text); },
            hoverFromSource: source =>
            {
                if (_vm is null || list is null || source.DataContext is not string text) return;
                var index = _vm.TimeOptions.IndexOf(text);
                if (index < 0 || index == getHoveredIndex()) return;
                setHoveredIndex(index);
                SetHighlightedContainer(list, getHighlighted, setHighlighted, index);
            });
    }

    /// <summary>
    /// Duration-space counterpart to <see cref="WireFlatTimePicker"/> — for Split's First/
    /// Second End fields, which bind to a PlannerEndTimeOption list (TimeText + Duration)
    /// instead of a plain flat list of strings, mirroring End's own bespoke wiring in the
    /// constructor above rather than End's methods being retrofitted onto this (see
    /// PlannerPageViewModel's ResolveApproxDurationFrom doc comment for why).
    /// </summary>
    private void WireDurationTimePicker(TextBox? box, ScrollViewer? scrollViewer, ItemsControl? list,
        Func<int?> getHoveredIndex, Action<int?> setHoveredIndex,
        Func<Border?> getHighlighted, Action<Border?> setHighlighted,
        Func<bool> isOpen, Action openViewModel, Action close, Action moveNext, Action movePrevious,
        Action<string> select, Func<int?> getScrollIndex, Func<ObservableCollection<PlannerEndTimeOption>?> getOptions)
    {
        void ScrollToWindow(int? centerIndex) => ScrollTimeDropdownToWindow(scrollViewer, list, isOpen, () => getOptions()?.Count ?? 0, getHighlighted, setHighlighted, centerIndex);

        WireTimePickerDropdown(box, list,
            open: () => { setHoveredIndex(null); openViewModel(); ScrollToWindow(getScrollIndex()); },
            moveNext: moveNext,
            movePrevious: movePrevious,
            close: close,
            commit: () =>
            {
                var options = getOptions();
                var index = getHoveredIndex() ?? getScrollIndex();
                if (options is not null && index is { } i && i < options.Count) select(options[i].TimeText);
                else close();
            },
            selectFromSource: source => { if (source.DataContext is PlannerEndTimeOption dto) select(dto.TimeText); },
            hoverFromSource: source =>
            {
                var options = getOptions();
                if (list is null || options is null || source.DataContext is not PlannerEndTimeOption dto) return;
                var index = options.IndexOf(dto);
                if (index < 0 || index == getHoveredIndex()) return;
                setHoveredIndex(index);
                SetHighlightedContainer(list, getHighlighted, setHighlighted, index);
            });
    }

    /// <summary>
    /// Shared wiring behind Start's and End's custom dropdowns: select-all-on-focus (so
    /// typing immediately replaces the old value), GotFocus/PointerPressed open it (same
    /// light-dismiss-reopen reasoning as WireTimeDropdown below), Up/Down/Enter/Escape are
    /// handled here rather than trusted to any list control's own keyboard navigation — the
    /// whole reason these fields stopped using AutoCompleteBox (see PlannerPageViewModel's
    /// StartTimeScrollIndex doc comment). Clicking an item in the popup commits it via
    /// <paramref name="selectFromSource"/>. Enter commits via <paramref name="commit"/>
    /// instead — a real repro showed typing "17" (a valid partial hour, dropdown showing
    /// "17:00") then pressing Enter just closed without ever writing "17:00" into the field,
    /// leaving the literal invalid text "17" behind. <paramref name="commit"/> is
    /// responsible for resolving whatever's currently highlighted (hovered, or the centered
    /// default) and committing that — Escape deliberately stays commit-less, a plain cancel.
    /// <paramref name="open"/> is responsible for both opening the ViewModel-side dropdown
    /// state AND explicitly triggering the initial scroll — see the caller's own comment on
    /// why that can't be left to StartTimeScrollIndex/EndTimeScrollIndex's PropertyChanged.
    /// </summary>
    private static void WireTimePickerDropdown(TextBox? box, ItemsControl? list,
        Action open, Action moveNext, Action movePrevious, Action close, Action commit,
        Action<StyledElement> selectFromSource, Action<StyledElement> hoverFromSource)
    {
        if (box is null) return;

        void Open()
        {
            box.SelectAll();
            open();
        }

        box.GotFocus += (_, _) => Open();
        box.AddHandler(InputElement.PointerPressedEvent, (_, _) => Open(), RoutingStrategies.Bubble, handledEventsToo: true);

        // Tunnel, not a plain KeyDown handler: since Avalonia 11.1-11.3 a TextBox with a selection
        // (Open() selects all) handles Down itself — collapsing the selection — before a bubbling
        // handler ever sees it. Tunnelling reaches the keys first, whatever the TextBox does (Gap 102).
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down:
                    moveNext();
                    e.Handled = true;
                    break;
                case Key.Up:
                    movePrevious();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    commit();
                    break;
                case Key.Escape:
                    close();
                    break;
            }
        }, RoutingStrategies.Tunnel);

        list?.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is StyledElement source) selectFromSource(source);
        }, RoutingStrategies.Bubble);

        // PointerMoved rather than PointerEntered/PointerExited on individual item
        // containers: those are Direct-routed in Avalonia (deliberately non-bubbling, same
        // as WPF's MouseEnter/MouseLeave), so a single handler up on the ItemsControl would
        // never see them. PointerMoved bubbles, and e.Source already identifies exactly
        // which item is under the pointer — the same DataContext-from-source trick
        // selectFromSource above already uses. Never resetting on PointerExited/pointer
        // leaving the list at all is deliberate — see the highlighted style's own comment.
        list?.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (e.Source is StyledElement source) hoverFromSource(source);
        }, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Shared machinery behind all six time fields' dropdowns — we own the ScrollViewer/
    /// ItemsControl directly by name (no native Popup.Host visual-tree traversal needed, the
    /// way AutoCompleteBox required). No SelectedIndex to reset either: Up/Down never touch
    /// any list control's own selection state at all (see WireTimePickerDropdown) — the
    /// crash class this whole replacement exists to avoid never had a foothold to begin with
    /// here. The item count is passed in rather than read off the control, since ItemsControl
    /// doesn't expose the same ItemCount API SelectingItemsControl does — callers read it
    /// straight off the ViewModel (TimeOptions.Count / EndTimeOptions.Count).
    /// </summary>
    private void ScrollTimeDropdownToWindow(ScrollViewer? scrollViewer, ItemsControl? list, Func<bool> isOpen, Func<int> getCount,
        Func<Border?> getHighlighted, Action<Border?> setHighlighted, int? centerIndex, int attemptsLeft = 5)
    {
        if (centerIndex is null || scrollViewer is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!isOpen()) return;

            var count = getCount();
            if (count == 0 || scrollViewer.Extent.Height <= 0)
            {
                if (attemptsLeft > 0) ScrollTimeDropdownToWindow(scrollViewer, list, isOpen, getCount, getHighlighted, setHighlighted, centerIndex, attemptsLeft - 1);
                return;
            }

            var (start, _) = PlannerDropdownWindow.Bounds(centerIndex.Value, count, 5);
            double itemHeight = scrollViewer.Extent.Height / count;

            // Derived at runtime rather than a hardcoded guess — this item template (plain
            // Border+TextBlock) doesn't render at the same row height AutoCompleteBox's own
            // internal template did (a prior hardcoded pixel constant was wrong twice over,
            // see DESIGN_GAP_HISTORY.md Gap 37/38). XAML's
            // MaxHeight="200" is only a placeholder to bound the flash before this first runs;
            // this overwrite makes the popup self-correct to exactly 5 rows regardless of
            // whatever the real rendered row height turns out to be.
            scrollViewer.MaxHeight = 5 * itemHeight;

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, start * itemHeight);

            // Opening the dropdown (or re-centering as you type) sets the highlight to the
            // current value's nearest slot; hoverFromSource (WireTimePickerDropdown) can move
            // it from there as the pointer moves, and nothing ever moves it back on its own —
            // see the "highlighted" style's own comment on why a live pointerover-based
            // approach was tried first and rejected.
            if (list is not null) SetHighlightedContainer(list, getHighlighted, setHighlighted, centerIndex);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Toggles the "highlighted" style class (see UserControl.Styles) onto whichever
    /// container holds <paramref name="index"/>, clearing it off wherever it previously was.
    /// The container is a plain ContentPresenter for an ItemsControl, not the Border itself —
    /// the Border is inside it, produced by the DataTemplate — so the actual styleable
    /// element is found via GetVisualDescendants. ContainerFromIndex always finds
    /// it here (never null for a valid index) because this ItemsControl uses the default,
    /// non-virtualizing panel — every item's container exists regardless of scroll position.
    /// </summary>
    private static void SetHighlightedContainer(ItemsControl list, Func<Border?> getHighlighted, Action<Border?> setHighlighted, int? index)
    {
        getHighlighted()?.Classes.Remove("highlighted");
        setHighlighted(null);

        if (index is null) return;

        var container = list.ContainerFromIndex(index.Value);
        var border = container?.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        border?.Classes.Add("highlighted");
        setHighlighted(border);
    }

    private void ScrollStartTimeDropdownToWindow(int? centerIndex) =>
        ScrollTimeDropdownToWindow(_formStartTimeScrollViewer, _formStartTimeList,
            () => _vm?.IsStartTimeDropDownOpen == true, () => _vm?.TimeOptions.Count ?? 0,
            () => _formStartTimeHighlighted, b => _formStartTimeHighlighted = b, centerIndex);

    private void ScrollEndTimeDropdownToWindow(int? centerIndex) =>
        ScrollTimeDropdownToWindow(_formEndTimeScrollViewer, _formEndTimeList,
            () => _vm?.IsEndTimeDropDownOpen == true, () => _vm?.EndTimeOptions.Count ?? 0,
            () => _formEndTimeHighlighted, b => _formEndTimeHighlighted = b, centerIndex);


    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var colList = new List<Grid>();
        for (int i = 0; i < 7; i++)
        {
            var grid = this.FindControl<Grid>($"DayCol{i}");
            if (grid != null) colList.Add(grid);
        }
        _dayColumns = colList.ToArray();

        if (_dayColumns.Length == 7)
        {
            BuildHourCells();
            if (_vm is not null) RebuildTiles();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Tiles.CollectionChanged -= OnTilesChanged;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as PlannerPageViewModel;

        if (_vm is not null)
        {
            _vm.Tiles.CollectionChanged += OnTilesChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            if (_dayColumns.Length == 7) RebuildTiles();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlannerPageViewModel.IsFormVisible) && _vm?.IsFormVisible == false)
            RemoveCreateGhost();

        switch (e.PropertyName)
        {
            case nameof(PlannerPageViewModel.StartTimeScrollIndex):
                if (_vm?.IsStartTimeDropDownOpen == true) ScrollStartTimeDropdownToWindow(_vm.StartTimeScrollIndex);
                break;
            case nameof(PlannerPageViewModel.EndTimeScrollIndex):
                if (_vm?.IsEndTimeDropDownOpen == true) ScrollEndTimeDropdownToWindow(_vm.EndTimeScrollIndex);
                break;
            case nameof(PlannerPageViewModel.SplitFirstStartTimeScrollIndex):
                if (_vm?.IsSplitFirstStartTimeDropDownOpen == true)
                    ScrollTimeDropdownToWindow(_splitFirstStartTimeScrollViewer, _splitFirstStartTimeList, () => _vm?.IsSplitFirstStartTimeDropDownOpen == true, () => _vm?.TimeOptions.Count ?? 0,
                        () => _splitFirstStartTimeHighlighted, b => _splitFirstStartTimeHighlighted = b, _vm.SplitFirstStartTimeScrollIndex);
                break;
            case nameof(PlannerPageViewModel.SplitFirstEndTimeScrollIndex):
                if (_vm?.IsSplitFirstEndTimeDropDownOpen == true)
                    ScrollTimeDropdownToWindow(_splitFirstEndTimeScrollViewer, _splitFirstEndTimeList, () => _vm?.IsSplitFirstEndTimeDropDownOpen == true, () => _vm?.SplitFirstEndTimeOptions.Count ?? 0,
                        () => _splitFirstEndTimeHighlighted, b => _splitFirstEndTimeHighlighted = b, _vm.SplitFirstEndTimeScrollIndex);
                break;
            case nameof(PlannerPageViewModel.SplitSecondStartTimeScrollIndex):
                if (_vm?.IsSplitSecondStartTimeDropDownOpen == true)
                    ScrollTimeDropdownToWindow(_splitSecondStartTimeScrollViewer, _splitSecondStartTimeList, () => _vm?.IsSplitSecondStartTimeDropDownOpen == true, () => _vm?.TimeOptions.Count ?? 0,
                        () => _splitSecondStartTimeHighlighted, b => _splitSecondStartTimeHighlighted = b, _vm.SplitSecondStartTimeScrollIndex);
                break;
            case nameof(PlannerPageViewModel.SplitSecondEndTimeScrollIndex):
                if (_vm?.IsSplitSecondEndTimeDropDownOpen == true)
                    ScrollTimeDropdownToWindow(_splitSecondEndTimeScrollViewer, _splitSecondEndTimeList, () => _vm?.IsSplitSecondEndTimeDropDownOpen == true, () => _vm?.SplitSecondEndTimeOptions.Count ?? 0,
                        () => _splitSecondEndTimeHighlighted, b => _splitSecondEndTimeHighlighted = b, _vm.SplitSecondEndTimeScrollIndex);
                break;
        }
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTiles();

    // ── Hour cell construction ────────────────────────────────────────────────

    private void BuildHourCells()
    {
        if (_vm is null || _dayColumns.Length < 7) return;

        for (int col = 0; col < 7; col++)
        {
            var dayGrid = _dayColumns[col];
            if (dayGrid is null) continue;

            dayGrid.Children.Clear();

            var stack = new StackPanel();
            for (int hour = PlannerPageViewModel.FirstHour; hour < PlannerPageViewModel.LastHour; hour++)
                stack.Children.Add(new Button { Classes = { "hourCell" } });

            // Transparent canvas sits on top and handles ALL pointer events for this column:
            // tile drag-move/resize (events bubble up from captured tile buttons) AND
            // empty-space drag-create.
            var canvas = new Canvas
            {
                Height           = _vm.GridContentHeight,
                Background       = Brushes.Transparent,
                IsHitTestVisible = true
            };
            canvas.Name = $"TilesCanvas{col}";

            var capturedCol = col;
            canvas.PointerPressed  += (s, e) => OnCanvasPointerPressed(s, e, capturedCol);
            canvas.PointerMoved    += OnCanvasPointerMoved;   // handles BOTH create AND tile drag
            canvas.PointerReleased += (s, e) => OnCanvasPointerReleased(s, e, capturedCol);

            dayGrid.Children.Add(stack);
            dayGrid.Children.Add(canvas);
        }
    }

    // ── Tile construction ─────────────────────────────────────────────────────

    private void RebuildTiles()
    {
        if (_vm is null || _dayColumns.Length != 7) return;

        foreach (var dayGrid in _dayColumns)
            foreach (var child in dayGrid.Children.OfType<Canvas>().ToList())
                child.Children.Clear();

        _createGhost        = null;
        _createGhostCanvas  = null;
        _ghostTimeLabel     = null;
        _tileGhost          = null;
        _tileGhostCanvas    = null;
        _tileGhostTimeLabel = null;
        _dragKind   = DragKind.None;
        _dragTile   = null;
        _dragButton = null;
        _dragCanvas = null;

        foreach (var tile in _vm.Tiles)
        {
            if (tile.ColumnIndex < 0 || tile.ColumnIndex > 6) continue;
            var dayGrid = _dayColumns[tile.ColumnIndex];
            var canvas  = dayGrid.Children.OfType<Canvas>().FirstOrDefault();
            if (canvas is null) continue;

            var btn = BuildTileButton(tile, canvas);
            Canvas.SetTop(btn, tile.Top);
            Canvas.SetLeft(btn, 2);
            btn.Height = tile.Height;
            btn.Width  = Math.Max(0, canvas.Bounds.Width - 4);
            canvas.Children.Add(btn);
        }

        foreach (var dayGrid in _dayColumns)
        {
            var canvas = dayGrid.Children.OfType<Canvas>().FirstOrDefault();
            if (canvas is null) continue;
            canvas.SizeChanged -= OnCanvasSizeChanged;
            canvas.SizeChanged += OnCanvasSizeChanged;
        }

        if (!_scrolledToCurrentTime)
            Dispatcher.UIThread.Post(ScrollToCurrentTime, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollToCurrentTime()
    {
        if (_vm is null || !_vm.IsTodayInView) return;

        var scroller = this.FindControl<ScrollViewer>("GridScroller");
        if (scroller is null) return;

        double contentH = _vm.GridContentHeight;
        double viewportH = scroller.Viewport.Height;
        if (viewportH <= 0) return;

        double targetY = _vm.CurrentTimeY - (viewportH / 2.0);
        double maxY = Math.Max(0, contentH - viewportH);
        targetY = Math.Clamp(targetY, 0, maxY);

        scroller.Offset = new Vector(scroller.Offset.X, targetY);
        _scrolledToCurrentTime = true;
    }

    private void OnCanvasSizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        var width = Math.Max(0, canvas.Bounds.Width - 4);
        foreach (var child in canvas.Children.OfType<Button>())
            child.Width = width;
        if (_createGhostCanvas == canvas && _createGhost is not null)
            _createGhost.Width = width;
        if (_tileGhostCanvas == canvas && _tileGhost is not null)
            _tileGhost.Width = width;
    }

    private static readonly IReadOnlyList<string> NoBadges = Array.Empty<string>();

    /// <summary>
    /// Maps a resolved Focus block outcome to its tile style class and badge text(s) — null
    /// class / no badges for a still-pending block (default focusTile gold). StartedLate and
    /// EndedEarly share one class deliberately: both are "something went off-schedule," same
    /// amber treatment, distinguished only by badge text. CompletedOnTime gets color with no
    /// badge — green alone is enough to read as "on time." wasStartedLate is independent of
    /// outcome (Classify gives EndedEarly priority when a block is both late and early), so an
    /// EndedEarly block that was also started late shows both badges stacked.
    /// </summary>
    private static (string? TileClass, IReadOnlyList<string> BadgeTexts) GetOutcomeVisual(FocusBlockOutcome? outcome, bool wasStartedLate) => outcome switch
    {
        FocusBlockOutcome.Missed => ("missedTile", new[] { "MISSED" }),
        FocusBlockOutcome.StartedLate => ("lateOrEarlyTile", new[] { "STARTED LATE" }),
        FocusBlockOutcome.EndedEarly => ("lateOrEarlyTile", wasStartedLate ? new[] { "STARTED LATE", "ENDED EARLY" } : new[] { "ENDED EARLY" }),
        FocusBlockOutcome.CompletedOnTime => ("completedOnTimeTile", NoBadges),
        _ => (null, NoBadges)
    };

    private Button BuildTileButton(PlannedBlockTileViewModel tile, Canvas ownerCanvas)
    {
        var btn = new Button
        {
            Classes      = { "blockTile", tile.IsFocus ? "focusTile" : "backgroundTile" },
            CornerRadius = new CornerRadius(6),
            DataContext  = tile
        };

        var (outcomeTileClass, outcomeBadgeTexts) = GetOutcomeVisual(tile.Outcome, tile.WasStartedLate);
        if (outcomeTileClass is not null)
            btn.Classes.Add(outcomeTileClass);

        // Unsealed Nen Contract draft blocks render distinctly — dashed white outline
        // (via a StrokeDashArray overlay, since Border has no native dash support) plus
        // a small "Draft" tag — until the contract is sealed and it becomes a real block.
        if (tile.IsDraft)
        {
            btn.Opacity = 0.85;
        }

        // Click → edit. Suppressed when _dragKind is Moving/Resizing because Button's class
        // handler raises Click before our PointerReleased clears the state.
        btn.Click += (_, _) =>
        {
            if (_dragKind is DragKind.Moving or DragKind.ResizingTop or DragKind.ResizingBottom) return;
            _vm?.EditBlockCommand.Execute(tile);
        };

        // Press captures the OWNER CANVAS (not the button), so all subsequent
        // PointerMoved/PointerReleased route straight to the canvas handlers.
        // Avalonia's Button marks PointerPressed as handled internally (that's what
        // drives its built-in ":pressed" dim effect), so a plain `+=` subscription
        // here would never run. handledEventsToo:true forces ours to run anyway.
        btn.AddHandler(
            InputElement.PointerPressedEvent,
            (s, e) => OnTilePointerPressed(s, e, tile, ownerCanvas),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        // PointerMoved on the button is cursor-feedback ONLY (hover state, no drag logic).
        btn.PointerMoved   += (s, e) => OnTileButtonPointerMoved(s, e, tile);

        var stack = new StackPanel { Spacing = 2 };

        var titleRow = new DockPanel { LastChildFill = true };
        titleRow.Children.Add(new TextBlock
        {
            Text              = tile.PresetName,
            FontSize          = 12,
            FontWeight        = FontWeight.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);
        stack.Children.Add(new TextBlock
        {
            Text            = $"{tile.Start:HH:mm} – {tile.End:HH:mm}",
            FontSize        = 10,
            Opacity         = 0.85
        });

        foreach (var badgeText in outcomeBadgeTexts)
        {
            stack.Children.Add(new TextBlock
            {
                Text       = badgeText,
                FontSize   = 9,
                FontWeight = FontWeight.Bold,
                Opacity    = 0.9
            });
        }

        if (tile.IsDraft)
        {
            stack.Children.Add(new TextBlock
            {
                Text       = "DRAFT",
                FontSize   = 9,
                FontWeight = FontWeight.Bold,
                Opacity    = 0.9
            });

            btn.Content = stack;
            return btn;
        }

        switch (tile.ContractBadge)
        {
            // A redemption block stays in its normal outcome/preset color family — a small
            // "REDEMPTION" tag is the only distinguishing mark, no border treatment.
            case PlannedBlockContractBadge.Redemption:
                stack.Children.Add(new TextBlock
                {
                    Text       = "REDEMPTION",
                    FontSize   = 9,
                    FontWeight = FontWeight.Bold,
                    Opacity    = 0.9
                });
                break;

            case PlannedBlockContractBadge.Bonus:
                stack.Children.Add(new TextBlock
                {
                    Text       = "BONUS",
                    FontSize   = 9,
                    FontWeight = FontWeight.Bold,
                    Opacity    = 0.9
                });
                break;
        }

        btn.Content = stack;
        return btn;
    }

    // ── Canvas pointer handlers ───────────────────────────────────────────────
    // The canvas handles ALL pointer movement: both empty-space drag-create and
    // tile move/resize (PointerMoved bubbles from the captured tile button to the canvas).

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e, int col)
    {
        if (_vm is null || sender is not Canvas canvas) return;
        if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
        // Only handle presses directly on the canvas (empty space).
        // Tile button presses set e.Handled = true, so they never reach here.
        if (e.Source is not Canvas) return;

        _vm.DragError = null;

        _dragKind       = DragKind.CreatePending;
        _dragStartPoint = e.GetPosition(canvas);
        _dragCanvas     = canvas;
        _dragTile       = null;
        _dragButton     = null;

        e.Pointer.Capture(canvas);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null || _dragCanvas is null) return;

        var pos = e.GetPosition(_dragCanvas);

        // ── Empty-space drag-create ───────────────────────────────────────────
        if (_dragKind is DragKind.CreatePending or DragKind.Creating)
        {
            if (_dragKind == DragKind.CreatePending)
            {
                if (Math.Abs(pos.Y - _dragStartPoint.Y) < DragThresholdPx) return;
                _dragKind = DragKind.Creating;
                ShowCreateGhost(_dragCanvas, _dragStartPoint.Y, pos.Y);
            }
            UpdateCreateGhost(_dragCanvas, _dragStartPoint.Y, pos.Y);
            e.Handled = true;
            return;
        }

        // ── Tile move / resize ────────────────────────────────────────────────
        // PointerMoved bubbles from the captured tile button up to this canvas handler.
        if (_dragKind is not (DragKind.MovePending or DragKind.Moving
            or DragKind.ResizingTopPending or DragKind.ResizingTop
            or DragKind.ResizingBottomPending or DragKind.ResizingBottom))
            return;

        if (_dragButton is null || _dragTile is null) return;

        var delta = pos - _dragStartPoint;

        // Promote Pending → active once threshold is exceeded, then show ghost overlay.
        if (_dragKind is DragKind.MovePending or DragKind.ResizingTopPending or DragKind.ResizingBottomPending)
        {
            if (Math.Abs(delta.Y) < DragThresholdPx) return;
            _dragKind = _dragKind switch
            {
                DragKind.MovePending           => DragKind.Moving,
                DragKind.ResizingTopPending    => DragKind.ResizingTop,
                DragKind.ResizingBottomPending => DragKind.ResizingBottom,
                _                              => _dragKind
            };
            _dragButton.Opacity = 0.3;
            ShowTileGhost(_dragCanvas, _tileOriginalTop, _tileOriginalHeight, _dragTile.IsFocus);
        }

        if (_tileGhost is null) { e.Handled = true; return; }

        double rawDeltaY = delta.Y;
        double totalH    = PlannerPageViewModel.HourRowHeight * PlannerPageViewModel.TotalHours;

        // ── Cross-day: track which column the pointer is over ─────────────
        // Use the absolute pointer position (pos.X), not the drag delta (delta.X),
        // so the tile snaps to the next column as soon as the pointer crosses the
        // boundary, regardless of where inside the tile the drag started.
        if (_dragKind is DragKind.MovePending or DragKind.Moving)
        {
            double canvasWidth = _dragCanvas.Bounds.Width;
            if (canvasWidth > 0)
            {
                int columnOffset = (int)Math.Floor(pos.X / canvasWidth);
                int targetColumn = Math.Clamp(_dragTile!.ColumnIndex + columnOffset, 0, 6);
                if (targetColumn != _effectiveDragColumn)
                {
                    _effectiveDragColumn = targetColumn;
                    MoveTileGhostToColumn(targetColumn);
                }
            }
        }

        switch (_dragKind)
        {
            case DragKind.Moving:
            {
                double newTop = Math.Clamp(
                    _tileOriginalTop + rawDeltaY,
                    0,
                    totalH - _tileOriginalHeight);
                Canvas.SetTop(_tileGhost, newTop);
                _tileGhost.Height = _tileOriginalHeight;
                UpdateTileGhostLabel();
                break;
            }
            case DragKind.ResizingTop:
            {
                double newTop = Math.Max(0, _tileOriginalTop + rawDeltaY);
                double newHeight = Math.Max(
                    PlannerLayout.MinimumTileHeightPx,
                    _tileOriginalHeight - (newTop - _tileOriginalTop));
                if (newTop >= _tileOriginalTop + _tileOriginalHeight) break;
                Canvas.SetTop(_tileGhost, newTop);
                _tileGhost.Height = newHeight;
                UpdateTileGhostLabel();
                break;
            }
            case DragKind.ResizingBottom:
            {
                double newHeight = Math.Clamp(
                    _tileOriginalHeight + rawDeltaY,
                    PlannerLayout.MinimumTileHeightPx,
                    totalH - _tileOriginalTop);
                _tileGhost.Height = newHeight;
                UpdateTileGhostLabel();
                break;
            }
        }

        e.Handled = true;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e, int col)
    {
        if (_vm is null) return;

        // ── Drag-create: click (no drag) ──────────────────────────────────────
        if (_dragKind == DragKind.CreatePending)
        {
            var canvas = _dragCanvas;
            var pos    = e.GetPosition(canvas);
            ClearDragState();
            if (canvas is null) { e.Handled = true; return; }

            var startTime = PlannerDragMath.YToSnappedTime(pos.Y, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
            var endTime   = startTime + TimeSpan.FromHours(1);
            (startTime, endTime) = PlannerDragMath.ClampRange(startTime, endTime, PlannerPageViewModel.FirstHour);

            double ghostTop    = (startTime.TotalMinutes - PlannerPageViewModel.FirstHour * 60.0) / 60.0 * PlannerPageViewModel.HourRowHeight;
            double ghostHeight = (endTime - startTime).TotalMinutes / 60.0 * PlannerPageViewModel.HourRowHeight;
            ShowClickGhost(canvas, ghostTop, ghostHeight, startTime, endTime);

            _vm.OpenNewBlockFormWithRangeCommand.Execute(new DragCreatedRange(col, startTime, endTime));
            e.Handled = true;
            return;
        }

        // ── Drag-create: dragged range ────────────────────────────────────────
        if (_dragKind == DragKind.Creating)
        {
            var canvas = _dragCanvas;
            var pos    = e.GetPosition(canvas);

            var startTime = PlannerDragMath.YToSnappedTime(
                Math.Min(_dragStartPoint.Y, pos.Y), PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
            var endTime = PlannerDragMath.YToSnappedTime(
                Math.Max(_dragStartPoint.Y, pos.Y), PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
            (startTime, endTime) = PlannerDragMath.ClampRange(startTime, endTime, PlannerPageViewModel.FirstHour);

            if (canvas is not null) UpdateCreateGhost(canvas, _dragStartPoint.Y, pos.Y);
            ClearDragState();

            if (canvas is not null)
                _vm.OpenNewBlockFormWithRangeCommand.Execute(new DragCreatedRange(col, startTime, endTime));
            e.Handled = true;
            return;
        }

        // ── Tile tap (threshold never crossed) → open editor ──────────────────
        if (_dragKind is DragKind.MovePending or DragKind.ResizingTopPending or DragKind.ResizingBottomPending)
        {
            var tile = _dragTile;
            ClearDragState();
            if (tile is not null) _vm.EditBlockCommand.Execute(tile);
            e.Handled = true;
            return;
        }

        // ── Tile move / resize commit ─────────────────────────────────────────
        if (_dragKind is DragKind.Moving or DragKind.ResizingTop or DragKind.ResizingBottom)
        {
            var tile = _dragTile;
            if (tile is null || _tileGhost is null) { ClearDragState(); e.Handled = true; return; }

            double finalTop    = Canvas.GetTop(_tileGhost);
            double finalBottom = finalTop + _tileGhost.Height;

            // A MOVE relocates a tile without ever resizing it, and a RESIZE only ever
            // moves the edge actually being dragged — the other edge is an anchor pinned at
            // its exact original (possibly non-quarter-hour, e.g. a 38-minute block) value.
            // Snapping BOTH edges independently from their pixel position, as the old code
            // did unconditionally, silently rounds an odd duration up/down to whatever the
            // two independent roundings happen to produce.
            var originalDayStart = _vm.WeekStart.AddDays(tile.ColumnIndex).Date;
            var originalStartOffset = tile.Start - originalDayStart;
            var originalEndOffset   = tile.End   - originalDayStart;

            TimeSpan newLocalStart, newLocalEnd;
            switch (_dragKind)
            {
                case DragKind.Moving:
                {
                    var duration = tile.End - tile.Start;
                    var snappedStart = PlannerDragMath.YToSnappedTime(finalTop, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                    newLocalStart = PlannerDragMath.ClampMoveStart(snappedStart, duration, PlannerPageViewModel.FirstHour);
                    newLocalEnd   = newLocalStart + duration;
                    break;
                }
                case DragKind.ResizingTop:
                {
                    var snappedStart = PlannerDragMath.YToSnappedTime(finalTop, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                    newLocalEnd   = originalEndOffset;
                    newLocalStart = PlannerDragMath.ClampResizeTopStart(snappedStart, newLocalEnd, PlannerPageViewModel.FirstHour);
                    break;
                }
                default: // ResizingBottom
                {
                    var snappedEnd = PlannerDragMath.YToSnappedTime(finalBottom, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                    newLocalStart = originalStartOffset;
                    newLocalEnd   = PlannerDragMath.ClampResizeBottomEnd(snappedEnd, newLocalStart, PlannerPageViewModel.FirstHour);
                    break;
                }
            }

            var dayDate     = _vm.WeekStart.AddDays(_effectiveDragColumn).Date;
            var newStartUtc = DateTime.SpecifyKind(dayDate.Add(newLocalStart), DateTimeKind.Local).ToUniversalTime();
            var newEndUtc   = DateTime.SpecifyKind(dayDate.Add(newLocalEnd),   DateTimeKind.Local).ToUniversalTime();

            var dayTiles = _vm.Tiles.Where(t => t.ColumnIndex == _effectiveDragColumn);
            if (PlannerDragMath.WouldCollide(tile.Id, tile.IsFocus, newStartUtc, newEndUtc, dayTiles))
            {
                SnapTileBack();
                ClearDragState();
                _vm.DragError = "Focus blocks cannot overlap — move rejected.";
                e.Handled = true;
                return;
            }

            ClearDragState();
            _vm.MoveOrResizeBlockCommand.Execute(new DragMoveResult(tile.Id, newStartUtc, newEndUtc));
            e.Handled = true;
            return;
        }

        ClearDragState();
    }

    // ── Tile pointer handlers ─────────────────────────────────────────────────

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e, PlannedBlockTileViewModel tile, Canvas ownerCanvas)
    {
        if (_vm is null || sender is not Button btn) return;
        if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
        if (_vm.IsTileFullyLocked(tile.Id)) { e.Handled = true; return; }

        _vm.DragError = null;

        var posInBtn    = e.GetPosition(btn);
        var posInCanvas = e.GetPosition(ownerCanvas);

        _dragStartPoint     = posInCanvas;
        _tileOriginalTop    = Canvas.GetTop(btn);
        _tileOriginalHeight = btn.Height; // explicitly-set property, never 0
        _dragButton          = btn;
        _dragCanvas          = ownerCanvas;
        _dragTile            = tile;
        _effectiveDragColumn = tile.ColumnIndex;

        if (posInBtn.Y <= ResizeHotZonePx)
            _dragKind = DragKind.ResizingTopPending;
        else if (posInBtn.Y >= btn.Height - ResizeHotZonePx)
            _dragKind = DragKind.ResizingBottomPending;
        else
            _dragKind = DragKind.MovePending;

        e.Pointer.Capture(ownerCanvas);
        e.Handled = true;
    }

    // Cursor-feedback ONLY — no drag logic here.
    // Actual drag handling lives in OnCanvasPointerMoved (PointerMoved bubbles from btn → canvas).
    private void OnTileButtonPointerMoved(object? sender, PointerEventArgs e, PlannedBlockTileViewModel tile)
    {
        if (_vm is null || _dragKind != DragKind.None || sender is not Button hoverBtn) return;

        if (_vm.IsTileFullyLocked(tile.Id))
        {
            hoverBtn.Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }

        var posInBtn  = e.GetPosition(hoverBtn);
        bool inBottom = posInBtn.Y >= hoverBtn.Height - ResizeHotZonePx;
        bool inTop    = posInBtn.Y <= ResizeHotZonePx;

        hoverBtn.Cursor = inBottom || inTop
            ? new Cursor(StandardCursorType.SizeNorthSouth)
            : new Cursor(StandardCursorType.SizeAll);
    }

    // ── Tile ghost visual ─────────────────────────────────────────────────────

    private void ShowTileGhost(Canvas canvas, double top, double height, bool isFocus)
    {
        var bgColor = isFocus ? Color.Parse("#B8922A") : Color.Parse("#E8E8F0");
        var fgBrush = isFocus
            ? (IBrush)Brushes.White
            : new SolidColorBrush(Color.Parse("#1A1A2E"));

        var label = new TextBlock
        {
            FontSize          = 10,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = fgBrush,
            Margin            = new Thickness(6, 4),
            TextWrapping      = TextWrapping.NoWrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        _tileGhostTimeLabel = label;

        _tileGhost = new Border
        {
            Background       = new SolidColorBrush(bgColor),
            CornerRadius     = new CornerRadius(6),
            Opacity          = 0.90,
            IsHitTestVisible = false,
            Child            = label
        };
        Canvas.SetLeft(_tileGhost, 2);
        Canvas.SetTop(_tileGhost, top);
        _tileGhost.Height = height;
        _tileGhost.Width  = Math.Max(0, canvas.Bounds.Width - 4);
        canvas.Children.Add(_tileGhost);
        _tileGhostCanvas = canvas;

        UpdateTileGhostLabel();
    }

    /// <summary>
    /// Moves the tile ghost from its current canvas to the target day column's canvas,
    /// preserving vertical position. No-op if the ghost is already on the target column.
    /// </summary>
    private void MoveTileGhostToColumn(int columnIndex)
    {
        if (_tileGhost is null || _tileGhostCanvas is null || _dayColumns.Length != 7) return;

        var newCanvas = _dayColumns[columnIndex].Children.OfType<Canvas>().FirstOrDefault();
        if (newCanvas is null || newCanvas == _tileGhostCanvas) return;

        double top    = Canvas.GetTop(_tileGhost);
        double height = _tileGhost.Height;

        _tileGhostCanvas.Children.Remove(_tileGhost);
        newCanvas.Children.Add(_tileGhost);
        _tileGhostCanvas = newCanvas;

        Canvas.SetTop(_tileGhost, top);
        Canvas.SetLeft(_tileGhost, 2);
        _tileGhost.Height = height;
        _tileGhost.Width  = Math.Max(0, newCanvas.Bounds.Width - 4);
    }

    // Mirrors OnCanvasPointerReleased's commit math exactly, so the live label shown while
    // dragging never promises a time range the release handler then commits differently —
    // a MOVE preserves the tile's exact original duration, a RESIZE pins whichever edge
    // isn't being dragged at its exact original (possibly non-quarter-hour) value.
    private void UpdateTileGhostLabel()
    {
        if (_tileGhost is null || _tileGhostTimeLabel is null || _dragTile is null || _vm is null) return;

        double top    = Canvas.GetTop(_tileGhost);
        double bottom = top + _tileGhost.Height;

        var originalDayStart = _vm.WeekStart.AddDays(_dragTile.ColumnIndex).Date;
        var originalStartOffset = _dragTile.Start - originalDayStart;
        var originalEndOffset   = _dragTile.End   - originalDayStart;

        TimeSpan startTime, endTime;
        switch (_dragKind)
        {
            case DragKind.Moving:
            {
                var duration = _dragTile.End - _dragTile.Start;
                var snappedStart = PlannerDragMath.YToSnappedTime(top, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                startTime = PlannerDragMath.ClampMoveStart(snappedStart, duration, PlannerPageViewModel.FirstHour);
                endTime   = startTime + duration;
                break;
            }
            case DragKind.ResizingTop:
            {
                var snappedStart = PlannerDragMath.YToSnappedTime(top, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                endTime   = originalEndOffset;
                startTime = PlannerDragMath.ClampResizeTopStart(snappedStart, endTime, PlannerPageViewModel.FirstHour);
                break;
            }
            default: // ResizingBottom
            {
                var snappedEnd = PlannerDragMath.YToSnappedTime(bottom, PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
                startTime = originalStartOffset;
                endTime   = PlannerDragMath.ClampResizeBottomEnd(snappedEnd, startTime, PlannerPageViewModel.FirstHour);
                break;
            }
        }

        _tileGhostTimeLabel.Text = $"{FormatTime(startTime)} – {FormatTime(endTime)}";
    }

    private void RemoveTileGhost()
    {
        if (_tileGhost is not null && _tileGhostCanvas is not null)
            _tileGhostCanvas.Children.Remove(_tileGhost);
        _tileGhost          = null;
        _tileGhostCanvas    = null;
        _tileGhostTimeLabel = null;
    }

    // ── Ghost: drag-create visual ─────────────────────────────────────────────

    private void ShowCreateGhost(Canvas canvas, double pressY, double currentY)
    {
        var label = new TextBlock
        {
            FontSize          = 10,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = Brushes.White,
            Margin            = new Thickness(6, 4),
            TextWrapping      = TextWrapping.NoWrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        _ghostTimeLabel = label;

        _createGhost = new Border
        {
            Background       = new SolidColorBrush(Color.Parse("#B8922A")),
            CornerRadius     = new CornerRadius(6),
            Opacity          = 0.70,
            IsHitTestVisible = false,
            Child            = label
        };
        Canvas.SetLeft(_createGhost, 2);
        _createGhost.Width = Math.Max(0, canvas.Bounds.Width - 4);
        canvas.Children.Add(_createGhost);
        _createGhostCanvas = canvas;

        UpdateCreateGhost(canvas, pressY, currentY);
    }

    private void ShowClickGhost(Canvas canvas, double top, double height, TimeSpan startTime, TimeSpan endTime)
    {
        RemoveCreateGhost();

        var label = new TextBlock
        {
            FontSize          = 10,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = Brushes.White,
            Margin            = new Thickness(6, 4),
            TextWrapping      = TextWrapping.NoWrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Text              = $"{FormatTime(startTime)} – {FormatTime(endTime)}"
        };
        _ghostTimeLabel = label;

        _createGhost = new Border
        {
            Background       = new SolidColorBrush(Color.Parse("#B8922A")),
            CornerRadius     = new CornerRadius(6),
            Opacity          = 0.70,
            IsHitTestVisible = false,
            Child            = label
        };
        Canvas.SetLeft(_createGhost, 2);
        Canvas.SetTop(_createGhost, top);
        _createGhost.Width  = Math.Max(0, canvas.Bounds.Width - 4);
        _createGhost.Height = height;
        canvas.Children.Add(_createGhost);
        _createGhostCanvas = canvas;
    }

    private void UpdateCreateGhost(Canvas canvas, double pressY, double currentY)
    {
        if (_createGhost is null) return;

        double top    = Math.Min(pressY, currentY);
        double bottom = Math.Max(pressY, currentY);
        Canvas.SetTop(_createGhost, top);
        _createGhost.Height = Math.Max(PlannerLayout.MinimumTileHeightPx, bottom - top);
        _createGhost.Width  = Math.Max(0, canvas.Bounds.Width - 4);

        if (_ghostTimeLabel is not null)
        {
            var startTime = PlannerDragMath.YToSnappedTime(
                Math.Min(pressY, currentY), PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
            var endTime = PlannerDragMath.YToSnappedTime(
                Math.Max(pressY, currentY), PlannerPageViewModel.FirstHour, PlannerPageViewModel.HourRowHeight);
            (startTime, endTime) = PlannerDragMath.ClampRange(startTime, endTime, PlannerPageViewModel.FirstHour);
            _ghostTimeLabel.Text = $"{FormatTime(startTime)} – {FormatTime(endTime)}";
        }
    }

    private void RemoveCreateGhost()
    {
        if (_createGhost is not null && _createGhostCanvas is not null)
            _createGhostCanvas.Children.Remove(_createGhost);
        _createGhost       = null;
        _createGhostCanvas = null;
        _ghostTimeLabel    = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SnapTileBack()
    {
        if (_dragButton is not null) _dragButton.Opacity = 1.0;
        RemoveTileGhost();
    }

    private void ClearDragState()
    {
        if (_dragButton is not null) _dragButton.Opacity = 1.0;
        RemoveTileGhost();
        _dragKind            = DragKind.None;
        _dragTile            = null;
        _dragButton          = null;
        _dragCanvas          = null;
        _effectiveDragColumn = 0;
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 24 ? "24:00" : $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
