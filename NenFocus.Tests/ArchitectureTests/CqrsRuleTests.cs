using System.Text.RegularExpressions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// CQRS (ARCHITECT_GOVERNANCE.md → Key patterns): all mutations are commands, all reads are queries.
/// A query that saves, commits or dispatches a command is a write hiding behind a read.
/// </summary>
public class CqrsRuleTests
{
    [Fact]
    public void QueryHandlersHoldNothingThatWrites()
    {
        var violations = typeof(Notifier).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("QueryHandler", StringComparison.Ordinal))
            .SelectMany(t => TypeUse.TypesUsedBy(t)
                .Where(u => u == typeof(IUnitOfWork) || u == typeof(ICommandDispatcher)
                            || u.IsGenericType && u.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// A command handler doesn't save a query's answer (Gap 106, decision 7): a query whose answer a
    /// command saves is a business rule in the wrong place — it belongs in the Domain, as attribution
    /// moved into <c>NenContract</c>. So no command handler holds a query handler or the query
    /// dispatcher. Validators are exempt: asking "is this allowed" is their job. Following the value
    /// itself into a save (data flow) was rejected — it is lost through conversions; this shuts the door.
    /// </summary>
    [Fact]
    public void CommandHandlersDoNotHoldQueryHandlers()
    {
        var violations = typeof(Notifier).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .SelectMany(t => TypeUse.TypesUsedBy(t)
                .Where(u => u == typeof(IQueryDispatcher)
                            || u.IsGenericType && u.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
                .Select(u => $"{t.Name} → {(u.IsGenericType ? u.GetGenericArguments()[0].Name : u.Name)}"));

        KnownExceptions.Check(violations, []);
    }

    // EF's own save, on anything.
    private static readonly Regex SaveCall = new(@"\.\s*(SaveChangesAsync|CommitAsync)\s*\(");
    // A field declared with a repository type in the same file — the repositories' writes are Add
    // and Remove (Gap 110), names a List shares, so only these receivers count. Fields only: a
    // constructor parameter is just copied into one, and its name may be reused by a local list.
    private static readonly Regex RepositoryVariable = new(@"\bI\w*Repository\s+(_\w+)");

    private static bool CallsAWriteMethod(string code) =>
        SaveCall.IsMatch(code)
        || RepositoryVariable.Matches(code)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Any(name => Regex.IsMatch(code, $@"(?<![\w.])(this\s*\.\s*)?{Regex.Escape(name)}\s*\.\s*(Add|Remove)\s*\("));

    [Fact]
    public void QueriesNeverCallAWriteMethod()
    {
        var violations = SourceCode.Files("NenFocus.Application/Queries")
            .Where(f => CallsAWriteMethod(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// A command member that must be set is `required`, not `= null!` + a validator NotNull rule: `null!`
    /// tells the compiler "never null" while the value can be, so the real check moved to runtime
    /// (Gap 88 close-out, lesson 13; phase 6 made Slot/First/Second required).
    /// </summary>
    [Fact]
    public void CommandsDoNotPromiseNonNullWithNullBang()
    {
        var violations = SourceCode.Files("NenFocus.Application")
            .Where(f => f.Path.EndsWith("Command.cs", StringComparison.Ordinal) && Regex.IsMatch(f.Code, @"=\s*null\s*!"))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// Every query has a caller outside its own slice and the tests (Gap 89 close-out): a query nothing
    /// asks is code nobody runs — Slice C found GetSessionHistoryQuery that way, holding a second "how long
    /// did it count" that disagreed with Session.CountedTime. A text scan for the query's name in the
    /// production projects, its own folder left out.
    /// </summary>
    [Fact]
    public void EveryQueryHasACaller()
    {
        var production = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus").ToList();
        var queries = typeof(Notifier).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)));

        var violations = queries
            .Where(q =>
            {
                var home = production.Single(f => f.Path.EndsWith($"/{q.Name}.cs", StringComparison.Ordinal)).Path;
                var folder = home[..(home.LastIndexOf('/') + 1)];
                var name = new Regex($@"\b{q.Name}\b");
                return !production.Any(f => !f.Path.StartsWith(folder, StringComparison.Ordinal) && name.IsMatch(f.Code));
            })
            .Select(q => q.Name);

        KnownExceptions.Check(violations, [
            // Break Timer Preset CRUD was built ahead of its screen; the Settings page (Gap 83) is its caller.
            "GetAllBreakTimerPresetsQuery",
        ]);
    }
}
