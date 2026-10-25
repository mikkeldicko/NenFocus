using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using NenFocus.ViewModels.Ritual;
using NenFocus.Views.Ritual;

namespace NenFocus.Tests.Headless;

/// <summary>The Ritual's first step, rendered for real: XAML, bindings and ViewModel together.</summary>
public class TenPageViewTests
{
    [AvaloniaFact]
    public void ShowsTheTenStepAndContinueReachesTheViewModel()
    {
        var continued = false;
        var view = new TenPageView { DataContext = new TenPageViewModel(() => continued = true, () => { }) };
        var window = new Window { Content = view };
        window.Show();

        var texts = view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain("Ten");

        var continueButton = view.GetLogicalDescendants().OfType<Button>()
            .Single(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Continue"));
        continueButton.Command!.Execute(null);

        continued.Should().BeTrue();
    }
}
