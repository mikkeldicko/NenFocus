using System.Text.RegularExpressions;
using NenFocus.Application.Interfaces;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// A repository is a collection of aggregates (Gap 110): <c>Add</c>, <c>Remove</c> and reads. A loaded
/// aggregate's changes are written by the unit of work that wraps every command, through EF's change
/// tracking — so there is no update or save method, and <c>Remove</c> takes the loaded aggregate
/// (a handler loads it, and asks it, before removing it).
/// </summary>
public class RepositoryRuleTests
{
    private static readonly Regex AllowedMember = new(@"^(Add|Remove|Get\w*|Has\w*|Exists\w*)$");

    [Fact]
    public void RepositoriesDeclareOnlyAddRemoveAndReads()
    {
        var violations = typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .SelectMany(i => i.GetMethods().Select(m => (Interface: i.Name, Member: m.Name))
                .Concat(i.GetProperties().Select(p => (Interface: i.Name, Member: p.Name)))
                .Concat(i.GetEvents().Select(e => (Interface: i.Name, Member: e.Name))))
            .Where(x => !AllowedMember.IsMatch(x.Member))
            .Select(x => $"{x.Interface}.{x.Member}");

        KnownExceptions.Check(violations, []);
    }

    // EF's own "write this whole object" calls, called on the context (`_db.Update(x)`,
    // `_db.Set<T>().Attach(x)`) or bare. Change tracking already knows what changed; marking the
    // whole object modified would hide a value-comparer gap (Gap 110 phase 1) instead of showing it.
    private static readonly Regex WholeObjectWrite = new(@"(?<![\w])(Update|UpdateRange|Attach|AttachRange)\s*\(|EntityState\.Modified");

    [Fact]
    public void RepositoriesNeverMarkAWholeObjectModified()
    {
        var violations = SourceCode.Files("NenFocus.Infrastructure/Persistence/Repositories")
            .Where(f => WholeObjectWrite.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }
}
