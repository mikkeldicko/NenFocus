using System.Text.RegularExpressions;
using NenFocus.Application.Services;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// One builder per DTO: "what the UI sees of this entity" is a rule, and each copy of the mapping
/// drifts on its own — three SessionDto builders already disagreed about Origin and
/// OriginatingBlockId (Gap 89 F1). A DTO built in several places gets one named factory on the DTO
/// (like SessionDto.MapBreaks) that the others call. Production code only; tests may build DTOs freely.
/// Limit: a target-typed `new()` or a `with { }` copy isn't tied to its type by a text scan.
/// </summary>
public class DtoBuilderRuleTests
{
    private static readonly string[] KnownMultipleBuilders =
    [
        // → Gap 89 area 4: StartSession, SessionStateService's restart path, GetActiveSessionQuery (dead) — already drifted (F1–F3).
        "SessionDto built in 3 files",
        // → Gap 95: EndSession, MarkFocusBlockMissed, GetUnacknowledgedPostSessions assemble credited minutes each (F6).
        "PostSessionDto built in 3 files",
        // → Gap 89 area 2: the Planner builds draft tiles as PlannedBlockDtos beside the day query.
        "PlannedBlockDto built in 2 files",
    ];

    [Fact]
    public void EachDtoIsBuiltInOnePlace()
    {
        var files = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus").ToList();

        var violations = typeof(Notifier).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal) && !t.IsNested)
            .Select(t => (t.Name, Builders: files.Count(f => new Regex($@"\bnew\s+{t.Name}\b(?!\s*\[)").IsMatch(f.Code))))
            .Where(x => x.Builders > 1)
            .Select(x => $"{x.Name} built in {x.Builders} files");

        KnownExceptions.Check(violations, KnownMultipleBuilders);
    }
}
