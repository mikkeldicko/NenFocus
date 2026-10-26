using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Services;
using NenFocus.Infrastructure.Persistence.Repositories;
using NSubstitute;
using NenFocus.ViewModels.Planner.ContractWizard;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Seal → real SQLite → GetNenContractForEdit → ContractEditPrefill: what the wizard receives
/// when an Advanced contract with a preset and a rest day is edited.
/// </summary>
public class ContractEditRoundTripTests : SqliteRepositoryTestBase
{
    private static readonly DateOnly Start = new(2026, 9, 29);

    [Fact]
    public async Task AdvancedContractWithPresetAndRestDay_ReopensWithAllOfThem()
    {
        var clock = TestClock.Create();
        clock.SetNow(Start.AddDays(-1).ToDateTime(new TimeOnly(16, 38)));
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetTimeZoneOffset(TimeSpan.Zero);

        var presetId = Guid.NewGuid();
        var restDay = Start.AddDays(2);
        var blocks = Enumerable.Range(0, 5)
            .Select(i => Start.AddDays(i))
            .Where(d => d != restDay)
            .Select(d => new NenContractBlockSpec(Guid.NewGuid(), At.Slot(d.ToDateTime(new TimeOnly(10, 0)), d.ToDateTime(new TimeOnly(12, 0))), IsFocus: true))
            .ToList();

        var blockRepo = new BlockRepository(Context);
        var seal = new SealNenContractCommandHandler(
            new NenContractRepository(Context), blockRepo,
            new ContractBlockBatchPreparer(),
            new SessionRepository(Context), new BlockPlacementChecker(), clock);

        var sealed_ = await seal.HandleAsync(new SealNenContractCommand
        {
            Name = "Test contract",
            Goal = "Goal",
            StartDate = Start,
            EndDate = Start.AddDays(4),
            StrictnessRule = StrictnessRule.DailyFlexibility,
            AllowCancellation = true,
            FixedDailyFloorHours = 2,
            RestDays = [restDay],
            WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ContractPresetId = presetId,
            Blocks = blocks
        });
        sealed_.Success.Should().BeTrue(sealed_.ErrorMessage);
        await Context.SaveChangesAsync();

        await using var readContext = CreateFreshContext();
        var edit = await new GetNenContractForEditQueryHandler(
                new NenContractRepository(readContext), new BlockRepository(readContext), new BlockPresetRepository(readContext))
            .HandleAsync(new GetNenContractForEditQuery(sealed_.ContractId!.Value));

        edit!.ContractPresetId.Should().Be(presetId);
        var prefill = ContractEditPrefill.From(edit);
        prefill.IsAdvanced.Should().BeTrue();
        prefill.FixedDailyFloorHours.Should().Be(2);
        prefill.RestDays.Should().Equal(restDay);
        prefill.WorkHoursStart.Should().Be(new TimeOnly(9, 0));
        prefill.WorkHoursEnd.Should().Be(new TimeOnly(18, 0));
    }
}
