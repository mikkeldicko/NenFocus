using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only the Domain decides whether a block may be changed or deleted (Gap 93). A contract's
/// strictness means one thing — how far a block may move — and that is answered in one place,
/// <c>ContractRules.MovementRange</c>; everything else asks <c>BlockChangeChecker</c> or reads the
/// block DTO's <c>Allowances</c>. So outside the Domain nothing *compares* a strictness value
/// (<c>== StrictnessRule.Locked</c>, a <c>switch</c> arm, <c>is StrictnessRule.X</c>): that
/// comparison is how the move, split and floor rules came to be written in three validators and
/// the Planner, each a little differently. Naming a value to pass it on (a command, seed data, a
/// picker's options) is not a comparison and doesn't count. A text scan: it sees these shapes,
/// not every way a decision could be dressed up — review covers the rest.
/// </summary>
public class ChangePermissionRuleTests
{
    private static readonly Regex StrictnessComparison = new(
        @"(==|!=)\s*StrictnessRule\.\w+" +          // x == StrictnessRule.Locked
        @"|StrictnessRule\.\w+\s*(==|!=)" +         // StrictnessRule.Locked == x
        @"|\bis\s+(not\s+)?StrictnessRule\.\w+" +   // x is StrictnessRule.Locked
        @"|StrictnessRule\.\w+\s*=>" +              // a switch arm
        @"|\bcase\s+StrictnessRule\.\w+");          // a switch case

    private static readonly string[] KnownComparisons =
    [
        // Display only: a strictness value turned into its name ("Lockdown", "Daily Flex",
        // "Period Study") — wording, not a decision. The converter is its home.
        "NenFocus/Converters/StrictnessRuleToLabelConverter.cs has 3",

        // The wizard's Review step holds a second copy of those three names → Gap 100 (one home
        // for user-facing text).
        "NenFocus/ViewModels/Planner/ContractWizard/ContractWizardViewModel.cs has 3",
    ];

    [Fact]
    public void NothingOutsideTheDomainComparesStrictnessValues()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Select(f => (f.Path, Count: StrictnessComparison.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownComparisons);
    }
}
