using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Infrastructure;

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
}
