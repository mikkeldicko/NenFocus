using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NenFocus.ViewModels.Pages;
using NenFocus.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace NenFocus.Views;

public partial class PlannerPageView : UserControl
{
    private Grid[] _dayColumns = Array.Empty<Grid>();
    private PlannerPageViewModel? _vm;

    public PlannerPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Use a more robust way to find the controls
        var colList = new List<Grid>();
        for (int i = 0; i < 7; i++)
        {
            var grid = this.FindControl<Grid>($"DayCol{i}");
            if (grid != null) colList.Add(grid);
        }

        _dayColumns = colList.ToArray();

        // CRITICAL: Check if we actually found all 7 columns before building
        if (_dayColumns.Length == 7)
        {
            BuildHourCells();
            if (_vm is not null)
            {
                RebuildTiles();
            }
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.Tiles.CollectionChanged -= OnTilesChanged;

        _vm = DataContext as PlannerPageViewModel;

        if (_vm is not null)
        {
            _vm.Tiles.CollectionChanged += OnTilesChanged;
            if (_dayColumns.Length == 7)
                RebuildTiles();
        }
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildTiles();
    }

    /// <summary>
    /// Build the 24 clickable hour cells for each of the 7 day columns. Done once.
    /// </summary>
    private void BuildHourCells()
    {
        if (_vm is null || _dayColumns.Length < 7) return;

        for (int col = 0; col < 7; col++)
        {
            var dayGrid = _dayColumns[col];
            if (dayGrid == null) continue; // Skip if null to avoid crash on Line 72

            dayGrid.Children.Clear();
            var stack = new StackPanel();
            for (int hour = PlannerPageViewModel.FirstHour; hour < PlannerPageViewModel.LastHour; hour++)
            {
                var cell = new Button
                {
                    Classes = { "hourCell" },
                    DataContext = _vm
                };
                var capturedCol = col;
                var capturedHour = hour;
                cell.Click += (_, _) =>
                {
                    _vm?.NewBlockAtCommand.Execute(new GridSlotPosition(capturedCol, capturedHour));
                };
                stack.Children.Add(cell);
            }

            // Canvas overlay for block tiles. Same height as the hour stack.
            var canvas = new Canvas
            {
                Height = _vm.GridContentHeight,
                Background = null,
                IsHitTestVisible = true
            };
            canvas.Name = $"TilesCanvas{col}";

            // Stack base + canvas overlay inside the day column Grid (single cell).
            dayGrid.Children.Add(stack);
            dayGrid.Children.Add(canvas);
        }
    }

    /// <summary>
    /// Clear and re-render the tiles on each day column from the VM's Tiles collection.
    /// </summary>
    private void RebuildTiles()
    {
        if (_vm is null || _dayColumns.Length != 7) return;

        // Clear existing tile canvases.
        foreach (var dayGrid in _dayColumns)
        {
            foreach (var child in dayGrid.Children.OfType<Canvas>().ToList())
                child.Children.Clear();
        }

        foreach (var tile in _vm.Tiles)
        {
            if (tile.ColumnIndex < 0 || tile.ColumnIndex > 6) continue;
            var dayGrid = _dayColumns[tile.ColumnIndex];
            var canvas = dayGrid.Children.OfType<Canvas>().FirstOrDefault();
            if (canvas is null) continue;

            var btn = BuildTileButton(tile);
            Canvas.SetTop(btn, tile.Top);
            Canvas.SetLeft(btn, 2);
            btn.Height = tile.Height;
            // Stretch tile to canvas width minus a small inset, kept in sync via SizeChanged.
            btn.Width = Math.Max(0, canvas.Bounds.Width - 4);
            canvas.Children.Add(btn);
        }

        // Make sure all tiles resize when the canvas does (one handler per canvas).
        foreach (var dayGrid in _dayColumns)
        {
            var canvas = dayGrid.Children.OfType<Canvas>().FirstOrDefault();
            if (canvas is null) continue;
            canvas.SizeChanged -= OnCanvasSizeChanged;
            canvas.SizeChanged += OnCanvasSizeChanged;
        }
    }

    private void OnCanvasSizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        var width = Math.Max(0, canvas.Bounds.Width - 4);
        foreach (var child in canvas.Children.OfType<Button>())
            child.Width = width;
    }

    private Button BuildTileButton(PlannedBlockTileViewModel tile)
    {
        var btn = new Button
        {
            Classes = { "blockTile", tile.IsFocus ? "focusTile" : "passiveTile" },
            CornerRadius = new Avalonia.CornerRadius(6),
            DataContext = tile
        };
        btn.Click += (_, _) =>
        {
            _vm?.EditBlockCommand.Execute(tile);
        };

        var stack = new StackPanel { Spacing = 2 };

        // Title row: profile name + optional AL (allowlist) chip
        var titleRow = new DockPanel { LastChildFill = true };
        if (tile.IsAllowlistMode)
        {
            var chip = new Border
            {
                Background = Avalonia.Media.Brushes.White,
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(4, 1),
                Margin = new Avalonia.Thickness(6, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "AL",
                    FontSize = 9,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = tile.IsFocus
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B8922A"))
                        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A1A2E"))
                }
            };
            DockPanel.SetDock(chip, Dock.Right);
            titleRow.Children.Add(chip);
        }
        titleRow.Children.Add(new TextBlock
        {
            Text = tile.ProfileName,
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = $"{tile.Start:HH:mm} – {tile.End:HH:mm}",
            FontSize = 10,
            Opacity = 0.85
        });

        btn.Content = stack;
        return btn;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}