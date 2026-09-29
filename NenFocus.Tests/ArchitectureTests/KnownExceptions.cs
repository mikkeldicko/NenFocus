using Xunit.Sdk;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// A rule that is broken today keeps a list of the places allowed to break it, and that list
/// may only shrink. Fails both ways: a violation not on the list is new drift; an entry that
/// no longer violates is stale and must be deleted, so the list can't quietly excuse a file
/// that later breaks the rule again. An empty list means the rule is complete.
/// </summary>
public static class KnownExceptions
{
    public static void Check(IEnumerable<string> violations, IEnumerable<string> exceptions)
    {
        var found = violations.ToHashSet(StringComparer.Ordinal);
        var allowed = exceptions.ToHashSet(StringComparer.Ordinal);

        var added = found.Except(allowed).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var stale = allowed.Except(found).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var problems = new List<string>();
        if (added.Count > 0)
            problems.Add("New violation (fix it, or if it is a deliberate exception add it to the list with a reason):\n  "
                         + string.Join("\n  ", added));
        if (stale.Count > 0)
            problems.Add("Listed exception no longer violates the rule (delete it from the list):\n  "
                         + string.Join("\n  ", stale));

        if (problems.Count > 0)
            throw new XunitException(string.Join("\n", problems));
    }
}
