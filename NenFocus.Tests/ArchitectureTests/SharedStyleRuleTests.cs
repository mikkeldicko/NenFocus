using System.Text.RegularExpressions;
using FluentAssertions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// The app's look has one definition per style (Gap 89 Slice B): what's in NenFocus/Styles is
/// never defined again in a view. A screen may add a local style for what differs — a listed
/// exception — but a second full copy is how Delete came to look different on two pages.
/// </summary>
public class SharedStyleRuleTests
{
    private static readonly Regex StyleSelector = new("<Style Selector=\"([^\"]+)\"");

    private static IEnumerable<(string Path, string Text)> Xaml(string folder) =>
        Directory.EnumerateFiles(Path.Combine(SourceCode.RepoRoot, folder), "*.axaml", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(SourceCode.RepoRoot, f).Replace('\\', '/'), File.ReadAllText(f)));

    private static readonly string[] LocalAdditions =
    [
        // Taller, centred buttons on the missed-sessions screen: two setters on top of the shared look.
        "NenFocus/Views/Recaps/MissedSessionsTriageView.axaml defines Button.primary",
        "NenFocus/Views/Recaps/MissedSessionsTriageView.axaml defines Button.secondary",
    ];

    [Fact]
    public void NoViewRedefinesASharedStyle()
    {
        var shared = Xaml("NenFocus/Styles")
            .SelectMany(f => StyleSelector.Matches(f.Text).Select(m => m.Groups[1].Value))
            .ToHashSet();
        shared.Should().Contain("Button.primary", "the scan must find the shared styles");

        var violations = Xaml("NenFocus/Views")
            .SelectMany(f => StyleSelector.Matches(f.Text)
                .Select(m => m.Groups[1].Value)
                .Where(shared.Contains)
                .Select(selector => $"{f.Path} defines {selector}"));

        KnownExceptions.Check(violations, LocalAdditions);
    }

    private const string DimBackdrop = "#88000000";

    private static readonly string[] FullFormOverlays =
    [
        // Whole forms over a dimmed screen, not a message and buttons — they keep their own card.
        "NenFocus/Views/Planner/PlannerPageView.axaml has 1",            // Split dialog
        "NenFocus/Views/Presets/BlockPresetPageView.axaml has 2",        // preset form, block-type picker
        "NenFocus/Views/Presets/ContractPresetPageView.axaml has 1",     // preset form
        "NenFocus/Views/Recaps/MissedSessionsTriageView.axaml has 1",    // the triage screen itself
    ];

    /// <summary>An "Are you sure?" or a notice is a ModalDialog (Gap 89 Slice B) — seven copies of
    /// the same card had drifted apart. Entries are "File has N", so a new hand-built dialog fails.</summary>
    [Fact]
    public void OnlyModalDialogDimsTheScreen()
    {
        var violations = Xaml("NenFocus/Views")
            .Where(f => f.Path != "NenFocus/Views/Controls/ModalDialog.axaml")
            .Select(f => (f.Path, Count: Regex.Matches(f.Text, DimBackdrop).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, FullFormOverlays);
    }
}
