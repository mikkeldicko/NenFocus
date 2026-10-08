using System.Reflection;
using System.Runtime.CompilerServices;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>Domain logic is synchronous (MODULE_STANDARDS → Domain → Forbidden).</summary>
public class DomainRuleTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void DomainHasNoAsync()
    {
        var violations = typeof(PolicyEngine).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(AllDeclared))
            .Where(m => m.GetCustomAttribute<AsyncStateMachineAttribute>() is not null || IsAwaitable(m.ReturnType))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}");

        KnownExceptions.Check(violations, []);
    }

    /// <summary>
    /// MODULE_STANDARDS → Contract First (Gap 111): an interface marks a port — something the outside
    /// world supplies, declared in Application. A Domain service is the rule itself, a sealed class
    /// whose public methods are its contract; nothing swaps it, so it gets no interface.
    /// </summary>
    [Fact]
    public void DomainDeclaresNoInterfaces()
    {
        var violations = typeof(PolicyEngine).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace?.StartsWith("NenFocus", StringComparison.Ordinal) == true)
            .Select(t => t.FullName!);

        KnownExceptions.Check(violations, []);
    }

    private static bool IsAwaitable(Type type) =>
        typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || type.IsGenericType && type.GetGenericTypeDefinition() is var d
           && (d == typeof(ValueTask<>) || d == typeof(IAsyncEnumerable<>));
}
