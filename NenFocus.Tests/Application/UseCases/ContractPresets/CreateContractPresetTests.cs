using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

/// <summary>Creating a contract preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class CreateContractPresetTests : UseCaseTestBase
{
    private static CreateContractPresetCommand ValidCommand() => new()
    {
        Name = "Study Month",
        StrictnessRule = StrictnessRule.Locked,
        DailyFloorHours = 4,
        AllowCancellation = false
    };

    private Task<ContractPreset?> StoredAsync() =>
        ReadAsync(context => context.ContractPresets.SingleOrDefaultAsync(p => p.Name == "Study Month"));

    [Fact]
    public async Task ValidCommand_StoresEveryField()
    {
        var result = await RunAsync(ValidCommand() with
        {
            StrictnessRule = StrictnessRule.PeriodTimeBudget,
            DailyFloorHours = 6,
            WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            AllowCancellation = true
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await FindAsync<ContractPreset>(result.PresetId!.Value);
        stored!.Name.Should().Be("Study Month");
        stored.StrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        stored.DailyFloor!.Hours.Should().Be(6);
        stored.WorkHours.Should().Be(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        stored.AllowCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task NoWorkHoursGiven_StoresNone()
    {
        await RunAsync(ValidCommand());

        (await StoredAsync())!.WorkHours.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)] // exactly fills a 10:00–15:00 window
    public async Task AFloorThatFits_IsAccepted(int? hours)
    {
        var result = await RunAsync(ValidCommand() with
        {
            DailyFloorHours = hours,
            WorkHours = new WorkHours(new TimeOnly(10, 0), new TimeOnly(15, 0))
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task NameAlreadyInUse_IsRefused()
    {
        await SeedAsync(ContractPreset.Create(Guid.NewGuid(), "Study Month", TestRules.Of(), workHours: null));

        var result = await RunAsync(ValidCommand());

        result.Success.Should().BeFalse();
        (await ReadAsync(context => context.ContractPresets.CountAsync(p => p.Name == "Study Month"))).Should().Be(1);
    }

    [Fact]
    public async Task EmptyName_IsRefused()
    {
        var result = await RunAsync(ValidCommand() with { Name = "" });

        result.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(25)]
    public async Task AFloorOutOfRange_IsRefused_AndNothingIsStored(int hours)
    {
        var result = await RunAsync(ValidCommand() with { DailyFloorHours = hours });

        result.Success.Should().BeFalse();
        (await StoredAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AFloorLongerThanTheWorkHours_IsRefused()
    {
        var result = await RunAsync(ValidCommand() with
        {
            DailyFloorHours = 6,
            WorkHours = new WorkHours(new TimeOnly(10, 0), new TimeOnly(15, 0)) // 5h window — a 6h floor could never fit
        });

        result.Success.Should().BeFalse();
    }
}
