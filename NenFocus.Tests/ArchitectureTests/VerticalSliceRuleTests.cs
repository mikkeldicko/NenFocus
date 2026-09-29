using System.Text.RegularExpressions;
using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Every use case is one folder, UseCases/&lt;Area&gt;/&lt;UseCase&gt;/, holding exactly a Command,
/// a CommandHandler, a Result and optionally a Validator (MODULE_STANDARDS → Vertical Slice Structure).
/// </summary>
public class VerticalSliceRuleTests
{
    private static readonly Type[] ApplicationTypes = typeof(Notifier).Assembly.GetTypes();

    /// <summary>The marker the DI scan matches handlers by — a command without it has no handler.</summary>
    [Fact]
    public void CommandsAndICommandGoTogether()
    {
        var commands = ApplicationTypes.Where(t => !t.IsInterface && !t.IsAbstract && t.Name.EndsWith("Command", StringComparison.Ordinal));
        var markers = ApplicationTypes.Where(t => !t.IsInterface && !t.IsAbstract && ImplementsICommand(t));

        var violations = commands.Where(t => !ImplementsICommand(t)).Select(t => $"{t.FullName}: named …Command but not ICommand<T>")
            .Concat(markers.Where(t => !t.Name.EndsWith("Command", StringComparison.Ordinal)).Select(t => $"{t.FullName}: ICommand<T> but not named …Command"));

        KnownExceptions.Check(violations, []);
    }

    private static bool ImplementsICommand(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static readonly Regex SliceFile = new(@"^NenFocus\.Application/UseCases/[^/]+/[^/]+/[^/]+\.cs$");

    [Fact]
    public void EveryUseCaseFolderIsOneSlice()
    {
        var files = SourceCode.Files("NenFocus.Application/UseCases").Select(f => f.Path).ToList();

        var misplaced = files.Where(p => !SliceFile.IsMatch(p)).Select(p => $"{p}: not in UseCases/<Area>/<UseCase>/");

        var badShape = files.Where(p => SliceFile.IsMatch(p))
            .GroupBy(p => p[..p.LastIndexOf('/')])
            .SelectMany(slice => ShapeProblems(slice.Select(p => p[(p.LastIndexOf('/') + 1)..]).ToList())
                .Select(problem => $"{slice.Key}: {problem}"));

        KnownExceptions.Check(misplaced.Concat(badShape), []);
    }

    private static IEnumerable<string> ShapeProblems(List<string> names)
    {
        int Count(string suffix) => names.Count(n => n.EndsWith(suffix, StringComparison.Ordinal));

        if (Count("Command.cs") != 1) yield return "needs exactly one …Command.cs";
        if (Count("CommandHandler.cs") != 1) yield return "needs exactly one …CommandHandler.cs";
        if (Count("Result.cs") != 1) yield return "needs exactly one …Result.cs";
        if (Count("Validator.cs") > 1) yield return "has more than one …Validator.cs";

        foreach (var other in names.Where(n => !new[] { "Command.cs", "CommandHandler.cs", "Result.cs", "Validator.cs" }
                     .Any(s => n.EndsWith(s, StringComparison.Ordinal))))
            yield return $"{other} is not one of the four slice files";
    }

    private static readonly Regex QuerySliceFile = new(@"^NenFocus\.Application/Queries/(?<slice>[^/]+)/(?<name>[^/]+)\.cs$");

    [Fact]
    public void EveryQueryFolderIsOneSlice()
    {
        var files = SourceCode.Files("NenFocus.Application/Queries").Select(f => f.Path).ToList();

        var misplaced = files.Where(p => !QuerySliceFile.IsMatch(p)).Select(p => $"{p}: not in Queries/<Name>/");

        var badShape = files.Where(p => QuerySliceFile.IsMatch(p))
            .GroupBy(p => QuerySliceFile.Match(p).Groups["slice"].Value)
            .SelectMany(slice =>
            {
                var names = slice.Select(p => QuerySliceFile.Match(p).Groups["name"].Value).ToList();
                var problems = new List<string>();
                if (names.Count(n => n.EndsWith("Query", StringComparison.Ordinal)) != 1) problems.Add("needs exactly one …Query.cs");
                if (names.Count(n => n.EndsWith("QueryHandler", StringComparison.Ordinal)) != 1) problems.Add("needs exactly one …QueryHandler.cs");
                return problems.Select(problem => $"NenFocus.Application/Queries/{slice.Key}: {problem}");
            });

        KnownExceptions.Check(misplaced.Concat(badShape), []);
    }

    private static readonly string[] SharedQueryResultExceptions =
    [
        // A business rule living in a query: Create/Update/Split block save its answer. Gap 88 phase 3
        // moves attribution into the Domain (it returns the final slot).
        "NenFocus.Application/Queries/ComputeRedemptionAttribution/RedemptionAttributionResult.cs",
    ];

    /// <summary>A result type kept in a query's folder belongs to that query alone; shared means DTOs/.
    /// Word match on the type name (= file name) across every other Application file.</summary>
    [Fact]
    public void QueryResultTypesAreNotSharedWithOtherSlices()
    {
        var application = SourceCode.Files("NenFocus.Application").ToList();

        var violations = application
            .Select(f => QuerySliceFile.Match(f.Path))
            .Where(m => m.Success)
            .Where(m => !m.Groups["name"].Value.EndsWith("Query", StringComparison.Ordinal)
                        && !m.Groups["name"].Value.EndsWith("QueryHandler", StringComparison.Ordinal))
            .Where(m =>
            {
                var slicePrefix = $"NenFocus.Application/Queries/{m.Groups["slice"].Value}/";
                var usage = new Regex($@"\b{Regex.Escape(m.Groups["name"].Value)}\b");
                return application.Any(f => !f.Path.StartsWith(slicePrefix, StringComparison.Ordinal) && usage.IsMatch(f.Code));
            })
            .Select(m => m.Value);

        KnownExceptions.Check(violations, SharedQueryResultExceptions);
    }

    /// <summary>A validator sits next to the command it validates — never in a shared Validators/ folder.</summary>
    [Fact]
    public void ValidatorsLiveInTheirCommandsSlice()
    {
        var violations = ApplicationTypes
            .Where(t => !t.IsAbstract && t.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Where(t => t.Namespace != t.BaseType!.GetGenericArguments()[0].Namespace)
            .Select(t => $"{t.FullName} validates {t.BaseType!.GetGenericArguments()[0].FullName}");

        KnownExceptions.Check(violations, []);
    }
}
