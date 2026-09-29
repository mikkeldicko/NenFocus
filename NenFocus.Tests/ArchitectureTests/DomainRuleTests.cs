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

    private static bool IsAwaitable(Type type) =>
        typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || type.IsGenericType && type.GetGenericTypeDefinition() is var d
           && (d == typeof(ValueTask<>) || d == typeof(IAsyncEnumerable<>));
}
