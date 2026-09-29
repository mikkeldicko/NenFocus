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
}
