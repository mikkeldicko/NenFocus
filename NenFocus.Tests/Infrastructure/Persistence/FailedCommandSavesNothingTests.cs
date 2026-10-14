using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Persistence.Repositories;
using NenFocus.Application.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 110: a failed command saves nothing, even when it changed a loaded aggregate before
/// failing — on real SQLite, through the real transaction wrapper and unit of work.
/// </summary>
public class FailedCommandSavesNothingTests : SqliteRepositoryTestBase
{
    /// <summary>Renames the preset it loads, then reports failure or success as told.</summary>
    private sealed class RenamingHandler(IBlockPresetRepository presets, bool fail)
        : ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult>
    {
        public async Task<UpdateBlockPresetResult> HandleAsync(UpdateBlockPresetCommand command, CancellationToken ct = default)
        {
            var preset = await presets.GetByIdAsync(command.PresetId, ct);
            preset!.Rename(command.Name);
            return fail ? UpdateBlockPresetResult.Fail("Refused after changing.") : UpdateBlockPresetResult.Ok();
        }
    }

    private static ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult> Wrapped(
        NenFocusDbContext scope, bool fail) =>
        new TransactionCommandHandlerDecorator<UpdateBlockPresetCommand, UpdateBlockPresetResult>(
            new RenamingHandler(new BlockPresetRepository(scope), fail),
            new EfCoreUnitOfWork(scope, Substitute.For<IDomainEventPublisher>()));

    private async Task<BlockPreset> SeedAsync(string name)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), name, WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(2026, 3, 2));
        Context.Add(preset);
        await Context.SaveChangesAsync();
        return preset;
    }

    private async Task<string> StoredNameAsync(Guid id)
    {
        await using var fresh = CreateFreshContext();
        return (await new BlockPresetRepository(fresh).GetByIdAsync(id))!.Name;
    }

    [Fact]
    public async Task AFailedCommand_ThatChangedAnAggregate_SavesNothing()
    {
        var preset = await SeedAsync("Original");

        await using var scope = CreateFreshContext();
        var result = await Wrapped(scope, fail: true).HandleAsync(new UpdateBlockPresetCommand { PresetId = preset.Id, Name = "Half-done" });

        result.Success.Should().BeFalse();
        (await StoredNameAsync(preset.Id)).Should().Be("Original");
    }

    [Fact]
    public async Task AFailedCommandsChanges_AreNotSavedByTheNextCommandInTheSameScope()
    {
        // The missed-block sweep runs several commands in one scope: a failed one's leftovers
        // must not ride along on the next one's commit.
        var failing = await SeedAsync("First");
        var succeeding = await SeedAsync("Second");

        await using var scope = CreateFreshContext();
        await Wrapped(scope, fail: true).HandleAsync(new UpdateBlockPresetCommand { PresetId = failing.Id, Name = "Half-done" });
        await Wrapped(scope, fail: false).HandleAsync(new UpdateBlockPresetCommand { PresetId = succeeding.Id, Name = "Second, renamed" });

        (await StoredNameAsync(failing.Id)).Should().Be("First");
        (await StoredNameAsync(succeeding.Id)).Should().Be("Second, renamed");
    }

    [Fact]
    public async Task ASucceedingCommand_SavesItsChanges()
    {
        var preset = await SeedAsync("Original");

        await using var scope = CreateFreshContext();
        await Wrapped(scope, fail: false).HandleAsync(new UpdateBlockPresetCommand { PresetId = preset.Id, Name = "Renamed" });

        (await StoredNameAsync(preset.Id)).Should().Be("Renamed");
    }
}
