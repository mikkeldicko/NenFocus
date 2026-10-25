using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only the Domain combines raw time fields. Comparing or subtracting Start/End/StartedAt/… outside
/// the Domain is a business question answered a second time ("has it started", "do they overlap",
/// "how long") — MODULE_STANDARDS → Application Handler Forbidden Zone, and the ViewModel's No Math.
///
/// Blocks and sessions hold a TimeSlot whose Start/End are internal (Gap 88, folding in Gap 90), so the
/// compiler already stops this on entities. It can't on DTOs: they carry public Moments
/// (SessionDto.StartedAt/EndAt, EndedAt, …) and Moment has public &lt; and −. Kept for that reason
/// (Gap 88 last step, 2026-09-30 — it caught ActiveSessionMonitor's own `EndAt - StartedAt` that day).
/// → Gap 89 area 4 turns the session DTOs' Moment pairs into a TimeSlot, leaving this test only
/// single moments to watch. A text scan: each entry carries a reason; entries are "File has N", so a
/// new comparison in an already-listed file is caught too. Repositories and the DbContext are left out:
/// SQL narrows through TimeSlot.Filters, and parity tests guard any copy of a rule.
/// </summary>
public class RawTimeFieldRuleTests
{
    private const string Field = @"\.(Start|End|StartedAt|EndedAt|RitualCompletedAt|SessionEndedAt|CancelledAt|StartsAt|EndsAt|EndAt)\b";

    // A comparison or subtraction on either side of the field — but not the "=>" of a lambda.
    private static readonly Regex RawUse = new(
        Field + @"\s*(<=|>=|(?<!=)>|<|==|!=|-(?![-=])|\+(?![+=]))"
        + @"|(<=|>=|(?<!=)>|<|==|!=|(?<![-=])-|(?<![+=])\+)\s*[A-Za-z_][A-Za-z0-9_.!?]*" + Field);

    // The same comparison written as a method call: FluentValidation's comparison rules pointed at a
    // time field (.GreaterThan(x => x.Start)), CompareTo / DateTime.Compare, and Math.Min/Max.
    private static readonly Regex RawUseAsCall = new(
        @"\.(GreaterThan|GreaterThanOrEqualTo|LessThan|LessThanOrEqualTo|Equal|NotEqual|InclusiveBetween|ExclusiveBetween)\s*\(\s*\w+\s*=>\s*[\w.!?]*" + Field
        + "|" + Field + @"\s*\.\s*CompareTo\s*\("
        + @"|\bDateTime\s*\.\s*Compare\s*\([^;]*" + Field
        + @"|\bMath\s*\.\s*(Min|Max)\s*\([^;]*" + Field);

    private static readonly string[] KnownRawUses =
    [
        // Allowed: drag/resize pixel geometry on tile positions (MODULE_STANDARDS → UI → Views).
        // Its business rules are caught by the local-time rule.
        "NenFocus/Views/Planner/PlannerPageView.axaml.cs has 6",
    ];

    [Fact]
    public void OnlyTheDomainCombinesRawTimeFields()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => !f.Path.StartsWith("NenFocus.Infrastructure/Persistence/", StringComparison.Ordinal))
            .Select(f => (f.Path, Count: RawUse.Matches(f.Code).Count + RawUseAsCall.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownRawUses);
    }
}
