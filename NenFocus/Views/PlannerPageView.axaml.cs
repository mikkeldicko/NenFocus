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

    public PlannerPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        // The six time fields are TimeOfDayBox controls — typing, their dropdowns and keys are theirs (Gap 89 Slice B).
    }

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
    /// badge — green alone is enough to read as "on time." StartedLate and EndedEarly are flags
    /// apart from the outcome (the outcome gives EndedEarly priority when a block is both), so a
    /// block that started late and ended early shows both badges stacked.
    /// </summary>
    // Colour from the one verdict; a label for each true fact — a block that started late and
    // ended early shows both (Gap 95).
    private static (string? TileClass, IReadOnlyList<string> BadgeTexts) GetOutcomeVisual(PlannedBlockTileViewModel tile)
    {
        var tileClass = tile.Outcome switch
        {
            FocusBlockOutcome.Missed => "missedTile",
            FocusBlockOutcome.StartedLate or FocusBlockOutcome.EndedEarly => "lateOrEarlyTile",
            FocusBlockOutcome.CompletedOnTime => "completedOnTimeTile",
            _ => null
        };

        var badges = new List<string>();
        if (tile.Outcome == FocusBlockOutcome.Missed) badges.Add("MISSED");
        if (tile.StartedLate) badges.Add("STARTED LATE");
        if (tile.EndedEarly) badges.Add("ENDED EARLY");
        return (tileClass, badges);
    }

    private Button BuildTileButton(PlannedBlockTileViewModel tile, Canvas ownerCanvas)
    {
        var btn = new Button
        {
            Classes      = { "blockTile", tile.IsFocus ? "focusTile" : "backgroundTile" },
            CornerRadius = new CornerRadius(6),
            DataContext  = tile
        };

        var (outcomeTileClass, outcomeBadgeTexts) = GetOutcomeVisual(tile);
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
            case PlannedBlockContractBadge.Committed:
                stack.Children.Add(new TextBlock
                {
                    Text       = "COMMITTED",
                    FontSize   = 9,
                    FontWeight = FontWeight.Bold,
                    Opacity    = 0.9
                });
                break;

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
            var newStartLocal = dayDate.Add(newLocalStart);
            var newEndLocal   = dayDate.Add(newLocalEnd);

            ClearDragState();
            _vm.MoveOrResizeBlockCommand.Execute(new DragMoveResult(tile.Id, newStartLocal, newEndLocal));
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
