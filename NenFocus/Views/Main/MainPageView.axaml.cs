using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NenFocus.ViewModels.Main;

namespace NenFocus.Views.Main;

public partial class MainPageView : UserControl
{
    private static readonly IBrush ShortBreakBrush = new SolidColorBrush(Color.Parse("#B8922A"));
    private static readonly IBrush LongBreakBrush = new SolidColorBrush(Color.Parse("#1A1A2E"));

    public MainPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainPageViewModel vm) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        RenderBreakMarkers(vm.BreakMarkers);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainPageViewModel.BreakMarkers)) return;
        if (DataContext is not MainPageViewModel vm) return;

        RenderBreakMarkers(vm.BreakMarkers);
    }

    /// <summary>
    /// Lays out one tick per break as a Grid column sized by the gap since the
    /// previous marker (or the start), so it stays correctly positioned regardless
    /// of the bar's actual rendered width — a ColumnDefinitions collection can't be
    /// data-bound directly in Avalonia, so this rebuilds it in code whenever
    /// BreakMarkers changes. Purely visual layout, not business logic — the
    /// fractions themselves already came from the session's BreakSchedule.
    /// </summary>
    private void RenderBreakMarkers(IReadOnlyList<BreakMark> markers)
    {
        var grid = BreakMarkersGrid;
        grid.ColumnDefinitions.Clear();
        grid.Children.Clear();

        if (markers.Count == 0) return;

        var previous = 0.0;
        foreach (var marker in markers.OrderBy(m => m.ProgressFraction))
        {
            var gap = Math.Max(0.0001, marker.ProgressFraction - previous);
            grid.ColumnDefinitions.Add(new ColumnDefinition(gap, GridUnitType.Star));

            var tickColumnIndex = grid.ColumnDefinitions.Count;
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var tick = new Border
            {
                Width = marker.Kind == BreakKind.LongBreak ? 5 : 3,
                Height = 8,
                CornerRadius = new CornerRadius(1.5),
                Background = marker.Kind == BreakKind.LongBreak ? LongBreakBrush : ShortBreakBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tick, tickColumnIndex);
            grid.Children.Add(tick);

            previous = marker.ProgressFraction;
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, 1 - previous), GridUnitType.Star));
    }
}
