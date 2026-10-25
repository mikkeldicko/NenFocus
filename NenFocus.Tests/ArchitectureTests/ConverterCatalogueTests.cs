using System.Text.RegularExpressions;
using Avalonia.Data.Converters;
using NenFocus.ViewModels.Shell;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// The MODULE_STANDARDS Converter Catalogue exists so a converter is reused before a second one
/// is written — which only works if it lists every converter. Code and catalogue must match.
/// </summary>
public class ConverterCatalogueTests
{
    private static readonly Regex CatalogueRow = new(@"^\| `(?<name>[A-Za-z]+Converter)` \|", RegexOptions.Multiline);

    private static List<string> Catalogue()
    {
        var standards = File.ReadAllText(Path.Combine(SourceCode.RepoRoot, "MODULE_STANDARDS.md"));
        var start = standards.IndexOf("## Converter Catalogue", StringComparison.Ordinal);
        var end = standards.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = standards[start..(end < 0 ? standards.Length : end)];
        return CatalogueRow.Matches(section).Select(m => m.Groups["name"].Value).ToList();
    }

    private static List<string> ConvertersInCode() =>
        typeof(ShellViewModel).Assembly.GetTypes()
            // One value or several (a MultiBinding of a start and an end) — both are converters (Gap 89 Slice B).
            .Where(t => !t.IsAbstract && (typeof(IValueConverter).IsAssignableFrom(t) || typeof(IMultiValueConverter).IsAssignableFrom(t)))
            .Select(t => t.Name)
            .ToList();

    [Fact]
    public void EveryConverterIsInTheCatalogue()
    {
        var catalogue = Catalogue();

        KnownExceptions.Check(ConvertersInCode().Where(c => !catalogue.Contains(c)), []);
    }

    [Fact]
    public void EveryCatalogueEntryStillExists()
    {
        var code = ConvertersInCode();

        KnownExceptions.Check(Catalogue().Where(c => !code.Contains(c)), []);
    }
}
