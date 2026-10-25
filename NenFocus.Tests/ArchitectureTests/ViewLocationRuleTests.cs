using Avalonia.Controls;
using FluentAssertions;
using NenFocus.ViewModels;
using NenFocus.ViewModels.Shell;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Views mirror ViewModels folder for folder, one folder per app area (Gap 89 Slice B):
/// NenFocus.ViewModels.Planner.PlannerPageViewModel is shown by NenFocus.Views.Planner.PlannerPageView.
/// The ViewLocator finds a screen only there, so a View in the wrong folder would show "Not Found".
/// Shared parts in Views.Controls have no ViewModel; MainWindow is created by the app itself.
/// </summary>
public class ViewLocationRuleTests
{
    private static readonly System.Reflection.Assembly Ui = typeof(ShellViewModel).Assembly;

    private static IEnumerable<Type> Screens() =>
        Ui.GetTypes().Where(t => typeof(Control).IsAssignableFrom(t)
                                 && t.Namespace?.StartsWith("NenFocus.Views", StringComparison.Ordinal) == true
                                 && t.Namespace != "NenFocus.Views.Controls"
                                 && t.Name.EndsWith("View", StringComparison.Ordinal));

    [Fact]
    public void EveryViewHasItsViewModelInTheMirroredFolder_AndTheLocatorFindsIt()
    {
        var violations = Screens()
            .Select(view => (view, viewModel: Ui.GetType(view.FullName!.Replace("NenFocus.Views.", "NenFocus.ViewModels.") + "Model")))
            .Where(x => x.viewModel is null
                        || !typeof(ViewModelBase).IsAssignableFrom(x.viewModel)
                        || ViewLocator.ViewNameFor(x.viewModel) != x.view.FullName)
            .Select(x => x.view.FullName!);

        KnownExceptions.Check(violations, []);
    }

    [Fact]
    public void NothingSitsLooseInViewsOrViewModels()
    {
        // Every screen and its ViewModel belong to an area folder; only the base class stays at the root.
        var violations = Ui.GetTypes()
            .Where(t => t.Namespace is "NenFocus.Views" or "NenFocus.ViewModels" && t != typeof(ViewModelBase)
                        && !t.Name.Contains('<') && !t.Name.StartsWith("XamlClosure", StringComparison.Ordinal))
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    [Fact]
    public void TheScreensAreFound()
    {
        // Guards the guard: a broken type scan would make the first test pass on nothing.
        Screens().Should().Contain(t => t.Name == "PlannerPageView")
            .And.Contain(t => t.Name == "ContractWizardView");
    }
}
