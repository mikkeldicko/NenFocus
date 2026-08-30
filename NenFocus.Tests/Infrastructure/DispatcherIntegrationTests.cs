using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Tests.Infrastructure;

/// <summary>
/// Proves the dispatchers work against the real composition root (AddInfrastructure), not
/// just hand-rolled test fixtures — and that a write dispatched through one fresh scope is
/// visible to a read dispatched through a different fresh scope right after, confirming
/// SQLite persistence (a real file, not an in-memory cache tied to one DbContext) makes the
/// "one DbContext per call" design safe rather than introducing a read-your-own-writes gap.
/// </summary>
public class DispatcherIntegrationTests
{
    private static ServiceProvider BuildRealProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusDispatcherIntegrationTest_{Guid.NewGuid()}.db"));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NenFocusDbContext>().Database.Migrate();

        return provider;
    }

    [Fact]
    public async Task CommandThenQuery_EachDispatchedThroughItsOwnFreshScope_QuerySeesTheCommittedWrite()
    {
        await using var provider = BuildRealProvider();
        var commandDispatcher = provider.GetRequiredService<ICommandDispatcher>();
        var queryDispatcher = provider.GetRequiredService<IQueryDispatcher>();

        var createResult = await commandDispatcher.DispatchAsync(new CreateBlockPresetCommand
        {
            Name = "Dispatcher Integration Test Preset",
            PresetType = BlockPresetType.Focus,
            WebsiteFilterType = WebsiteFilterType.Blocklist,
            Websites = ["example.com"]
        });

        createResult.Success.Should().BeTrue(createResult.ErrorMessage);

        var presets = await queryDispatcher.QueryAsync(new GetAllBlockPresetsQuery());

        presets.Should().Contain(p => p.Id == createResult.PresetId && p.Name == "Dispatcher Integration Test Preset");
    }
}
