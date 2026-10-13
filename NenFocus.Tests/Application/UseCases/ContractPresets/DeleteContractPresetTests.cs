using FluentAssertions;
using NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

/// <summary>Deleting a contract preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class DeleteContractPresetTests : UseCaseTestBase
{
    private static readonly DateOnly Today = new(2026, 1, 5);

    public DeleteContractPresetTests() => Clock.SetNow(Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

    private static ContractPreset Preset() => ContractPreset.Create(Guid.NewGuid(), "Study Month", TestRules.Of(), workHours: null);

    private Task<DeleteContractPresetResult> DeleteAsync(Guid presetId) =>
        RunAsync(new DeleteContractPresetCommand { PresetId = presetId });

    [Fact]
    public async Task NoContractInEffectFromIt_IsRemoved()
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<ContractPreset>(preset.Id)).Should().BeNull();
    }

    [Fact]
    public async Task AlreadyGone_Succeeds()
    {
        var result = await DeleteAsync(Guid.NewGuid());

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task AContractInEffectWasMadeFromIt_IsRefused_AndKept()
    {
        var preset = Preset();
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(), name: "Contract", goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today, Today.AddDays(6), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(), allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone,
            contractPresetId: preset.Id);
        await SeedAsync(preset, contract);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<ContractPreset>(preset.Id)).Should().NotBeNull();
    }
}
