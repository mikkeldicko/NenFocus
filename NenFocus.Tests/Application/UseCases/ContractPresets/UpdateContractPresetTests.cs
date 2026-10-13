using FluentAssertions;
using NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

/// <summary>Updating a contract preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class UpdateContractPresetTests : UseCaseTestBase
{
    private static ContractPreset Preset(string name = "Original") =>
        ContractPreset.Create(Guid.NewGuid(), name, TestRules.Of(fixedDailyFloor: new DailyFloor(4)), workHours: null);

    private static UpdateContractPresetCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        Name = "Renamed",
        StrictnessRule = StrictnessRule.DailyFlexibility,
        DailyFloorHours = 5,
        AllowCancellation = true
    };

    private async Task<ContractPreset> StoredAsync(ContractPreset preset) => (await FindAsync<ContractPreset>(preset.Id))!;

    [Fact]
    public async Task ValidCommand_StoresEveryField()
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await RunAsync(ValidCommand(preset.Id) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)) });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await StoredAsync(preset);
        stored.Name.Should().Be("Renamed");
        stored.StrictnessRule.Should().Be(StrictnessRule.DailyFlexibility);
        stored.DailyFloor!.Hours.Should().Be(5);
        stored.WorkHours.Should().Be(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        stored.AllowCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task KeepingItsOwnName_IsNotACollision()
    {
        var preset = Preset("Renamed");
        await SeedAsync(preset);

        var result = await RunAsync(ValidCommand(preset.Id));

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task KeepBlockingDuringBreaks_IsStoredAndSurvivesALaterEdit()
    {
        // Regression (Gap 64, trap 1): the handler never passed this rule to Update, which
        // defaulted it — so editing any preset silently reset it to breaks lifting blocking.
        var preset = Preset();
        await SeedAsync(preset);

        await RunAsync(ValidCommand(preset.Id) with { KeepBlockingDuringBreaks = true });
        (await StoredAsync(preset)).KeepBlockingDuringBreaks.Should().BeTrue();

        await RunAsync(ValidCommand(preset.Id) with { Name = "Renamed again", KeepBlockingDuringBreaks = true });
        (await StoredAsync(preset)).KeepBlockingDuringBreaks.Should().BeTrue();
    }

    [Fact]
    public async Task BonusBlocksFollowStrictness_IsStored()
    {
        var preset = Preset();
        await SeedAsync(preset);

        await RunAsync(ValidCommand(preset.Id) with { BonusBlocksFollowStrictness = true });

        (await StoredAsync(preset)).Rules.BonusBlocksFollowStrictness.Should().BeTrue();
    }

    [Fact]
    public async Task PresetNotFound_Fails()
    {
        var result = await RunAsync(ValidCommand(Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyPresetId_IsRefused()
    {
        var result = await RunAsync(ValidCommand(Guid.Empty));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task NameInUseByAnotherPreset_IsRefused_AndNothingChanges()
    {
        var preset = Preset();
        await SeedAsync(preset, Preset("Renamed"));

        var result = await RunAsync(ValidCommand(preset.Id));

        result.Success.Should().BeFalse();
        (await StoredAsync(preset)).Name.Should().Be("Original");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(25)]
    [InlineData(99)]
    public async Task AFloorOutOfRange_IsRefused_AndNothingChanges(int hours)
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await RunAsync(ValidCommand(preset.Id) with { DailyFloorHours = hours });

        result.Success.Should().BeFalse();
        (await StoredAsync(preset)).DailyFloor!.Hours.Should().Be(4);
    }

    [Fact]
    public async Task NoFloor_IsAccepted()
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await RunAsync(ValidCommand(preset.Id) with { DailyFloorHours = null });

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task AFloorLongerThanTheWorkHours_IsRefused()
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await RunAsync(ValidCommand(preset.Id) with
        {
            DailyFloorHours = 6,
            WorkHours = new WorkHours(new TimeOnly(10, 0), new TimeOnly(15, 0))
        });

        result.Success.Should().BeFalse();
    }
}
