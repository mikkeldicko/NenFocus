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

        KnownExceptions.Check(violations, CommandHandlersHoldingQueryHandlers);
    }

    private static readonly string[] CommandHandlersHoldingQueryHandlers =
    [
        // The recap is handed back to the screen that shows it, never saved.
        "BreakNenContractCommandHandler → GetNenContractRecapQuery",
        "CompleteNenContractCommandHandler → GetNenContractRecapQuery",
        // Readiness decides whether to complete — a Domain answer (NenContract.CheckComplete) behind a loader.
        "CompleteNenContractCommandHandler → GetNenContractCompletionReadinessQuery",
    ];

    // The repositories' write methods, and EF's own save.
    private static readonly Regex WriteCall = new(@"\.\s*(AddAsync|UpdateAsync|DeleteAsync|SaveAsync|SaveChangesAsync|CommitAsync)\s*\(");

    [Fact]
    public void QueriesNeverCallAWriteMethod()
    {
        var violations = SourceCode.Files("NenFocus.Application/Queries")
            .Where(f => WriteCall.IsMatch(f.Code))
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
}
