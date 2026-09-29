using System.Text;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Reads the solution's own source so a rule can ban a call reflection can't see
/// (<c>DateTime.UtcNow</c>, <c>ToLocalTime()</c>): a type rule only knows "uses DateTime".
/// Comments are stripped so documentation that mentions a banned call doesn't count.
/// </summary>
public static class SourceCode
{
    public sealed record File(string Path, string Code);

    public static readonly string RepoRoot = FindRepoRoot();

    /// <summary>Every .cs file in the given projects, comments stripped. Path is repo-relative with '/'.
    /// Skips build output and EF migrations (generated, and the only place raw string literals appear).</summary>
    public static IEnumerable<File> Files(params string[] projects) =>
        projects
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoRoot, p), "*.cs", SearchOption.AllDirectories))
            .Select(full => Path.GetRelativePath(RepoRoot, full).Replace('\\', '/'))
            .Where(p => !p.Contains("/obj/") && !p.Contains("/bin/") && !p.Contains("/Migrations/"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new File(p, WithoutComments(System.IO.File.ReadAllText(Path.Combine(RepoRoot, p)))));

    /// <summary>Removes //, /// and /* */ comments; keeps strings, char literals and every newline.</summary>
    public static string WithoutComments(string code)
    {
        var result = new StringBuilder(code.Length);
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
            }
            else if (c == '/' && next == '*')
            {
                i += 2;
                while (i < code.Length && !(code[i] == '*' && i + 1 < code.Length && code[i + 1] == '/'))
                {
                    if (code[i] == '\n') result.Append('\n');
                    i++;
                }
                i += 2;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && code[i - 1] == '@' || i > 1 && code[i - 1] == '$' && code[i - 2] == '@';
                i = CopyLiteral(code, i, '"', verbatim, result);
            }
            else if (c == '\'')
            {
                i = CopyLiteral(code, i, '\'', verbatim: false, result);
            }
            else
            {
                result.Append(c);
                i++;
            }
        }
        return result.ToString();
    }

    // Copies a literal from its opening quote through its closing one; returns the index after it.
    // Regular literals escape with '\'; verbatim strings escape a quote by doubling it.
    private static int CopyLiteral(string code, int start, char quote, bool verbatim, StringBuilder result)
    {
        result.Append(quote);
        var i = start + 1;
        while (i < code.Length)
        {
            var c = code[i];
            if (!verbatim && c == '\\' && i + 1 < code.Length)
            {
                result.Append(c).Append(code[i + 1]);
                i += 2;
                continue;
            }
            result.Append(c);
            i++;
            if (c == quote)
            {
                if (verbatim && i < code.Length && code[i] == quote)
                {
                    result.Append(quote);
                    i++;
                    continue;
                }
                break;
            }
        }
        return i;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "NenFocus.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("NenFocus.slnx not found above the test output folder.");
    }
}
