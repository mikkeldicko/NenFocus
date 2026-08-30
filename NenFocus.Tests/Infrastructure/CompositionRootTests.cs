using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NenFocus.ViewModels;

namespace NenFocus.Tests.Infrastructure;

/// <summary>
/// Builds the real composition root — the exact AddInfrastructure call Program.cs uses,
/// plus AddLogging() since Host.CreateDefaultBuilder wires that up automatically in the
/// real app but a bare ServiceCollection doesn't — with ValidateOnBuild AND ValidateScopes,
/// so both a missing DI registration (Gap 12's failure mode: INenContractRepository had no
/// registration at all, and nothing in the test suite caught it because every other test
/// mocks interfaces directly rather than resolving them through the real container) and a
/// captive-dependency lifetime mismatch (a Singleton capturing a Scoped service — found and
/// fixed the same day: SessionStateService was injecting ISessionRepository/
/// IBlockPresetRepository directly instead of resolving them per-operation via
/// IServiceScopeFactory, the pattern its siblings ActiveSessionMonitor and
/// ScheduledEnforcementService already used correctly) fail a test immediately.
/// </summary>
public class CompositionRootTests
{
    [Fact]
    public void AddInfrastructure_AllRegisteredServicesResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusCompositionRootTest_{Guid.NewGuid()}.db"));

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        act.Should().NotThrow();
    }

    // ValidateOnBuild only checks the DI graph's shape (a missing registration, an obvious
    // lifetime mismatch on a constructor parameter) — it does NOT actually resolve anything,
    // so it never caught NenFocusDbContext itself failing to resolve from a real scope
    // (AddDbContextFactory + AddDbContext registered independently for the same context type
    // silently broke this — see DESIGN_GAP_HISTORY.md). This test actually resolves it.
    [Fact]
    public void AddInfrastructure_NenFocusDbContext_ResolvesFromARealScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusCompositionRootTest_{Guid.NewGuid()}.db"));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<NenFocusDbContext>();

        act.Should().NotThrow();
    }

    // The actual regression guard for the shared-DbContext concurrency bug this fixed
    // (DESIGN_GAP_HISTORY.md): ShellViewModel used to constructor-inject 34 individual
    // IQueryHandler/ICommandHandler instances, resolved once from the DI root (App.axaml.cs
    // never creates a scope) — meaning every Scoped-backed repository underneath them shared
    // one NenFocusDbContext for the app's entire runtime, silently, because Host.CreateDefaultBuilder
    // only enables ValidateScopes in the "Development" environment and nothing here ever sets
    // it. Two overlapping operations against that one context (a poll timer, a session-ended
    // refresh, a user Save, all plausible together) throw EF Core's "a second operation was
    // started on this context" — silently, since every call site is fire-and-forget via
    // Dispatcher.UIThread.Post with no try/catch. Migrating every ViewModel onto
    // IQueryDispatcher/ICommandDispatcher (each dispatch gets its own fresh scope) closes this
    // for the whole ViewModel tree at once — this test resolves ShellViewModel exactly the way
    // App.axaml.cs does (root provider, Transient, no scope) and would fail immediately if a
    // future change reintroduced a directly-injected Scoped-backed handler anywhere in the tree.
    [Fact]
    public void AddInfrastructure_ShellViewModel_ResolvesFromRootProviderLikeAppAxamlDoes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusCompositionRootTest_{Guid.NewGuid()}.db"));
        services.AddTransient<ShellViewModel>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var act = () => provider.GetRequiredService<ShellViewModel>();

        act.Should().NotThrow();
    }
}
