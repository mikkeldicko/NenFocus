using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NenFocus.Application.Interfaces;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Answers queries from a table of canned results, so a view can be rendered with the data it
/// needs and nothing else. A query with no entry gets an empty list when it returns a list,
/// otherwise its default — a screen's side loads (contracts, deficits) simply see "nothing".
/// </summary>
internal sealed class FakeQueryDispatcher : IQueryDispatcher
{
    private readonly Dictionary<Type, Func<object, object?>> _answers = new();

    public FakeQueryDispatcher Answer<TQuery, TResult>(Func<TQuery, TResult> answer) where TQuery : IQuery<TResult>
    {
        _answers[typeof(TQuery)] = q => answer((TQuery)q);
        return this;
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        if (_answers.TryGetValue(query.GetType(), out var answer))
            return Task.FromResult((TResult)answer(query)!);

        var type = typeof(TResult);
        if (type.IsGenericType && type.GetGenericArguments() is [var item] && type.IsAssignableFrom(item.MakeArrayType()))
            return Task.FromResult((TResult)(object)Array.CreateInstance(item, 0));

        return Task.FromResult(default(TResult)!);
    }
}

/// <summary>Showing a view in a headless window, and finding and clicking what's on it.</summary>
internal static class HeadlessTestKit
{
    public static Window Show(Control view, double width = 1200, double height = 900)
    {
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Settle();
        return window;
    }

    /// <summary>Runs queued UI work (bindings, async loads that completed at once, layout).</summary>
    public static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Whether the user can see the control: it and every logical parent are visible.
    /// IsEffectivelyVisible alone isn't enough — a control under a hidden panel can still report true.
    /// </summary>
    public static bool IsShown(Control control) =>
        control.IsVisible && control.GetLogicalAncestors().OfType<Control>().All(a => a.IsVisible);

    /// <summary>Texts of every TextBlock the user can see under root.</summary>
    public static List<string> VisibleTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => IsShown(t) && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.Text!)
            .ToList();

    /// <summary>The one shown button carrying this text — the innermost, when a card button holds a smaller one.</summary>
    public static Button ButtonWithText(ILogical root, string text)
    {
        bool Carries(Button b) => b.Content as string == text
            || b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == text);

        var matches = root.GetLogicalDescendants().OfType<Button>().Where(b => IsShown(b) && Carries(b)).ToList();
        return matches.Single(b => !b.GetLogicalDescendants().OfType<Button>().Any(matches.Contains));
    }

    /// <summary>A real mouse click on the control's centre — hit testing, pointer events and all.</summary>
    public static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("Control is not in the window.");
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle();
    }

    /// <summary>Focuses a text box and types into it the way a keyboard does.</summary>
    public static void Type(Window window, TextBox box, string text)
    {
        box.Focus();
        Settle();
        window.KeyTextInput(text);
        Settle();
    }

    public static void PressKey(Control target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        Settle();
    }
}
