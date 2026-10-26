using FluentAssertions;
using NenFocus.Application.UseCases.NenContracts.SaveReflection;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>Saving a contract's reflection, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class SaveNenContractReflectionCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    public SaveNenContractReflectionCommandHandlerTests() => Clock.SetNow(EndDate.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

    private static NenContract CompletedContract()
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        contract.Complete(At.Utc(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        return contract;
    }

    [Fact]
    public async Task ContractNotFound_Fails()
    {
        var result = await RunAsync(new SaveNenContractReflectionCommand { ContractId = Guid.NewGuid(), Reflection = "Went well" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CompletedContract_StoresTheTrimmedReflection()
    {
        var contract = CompletedContract();
        await SeedAsync(contract);

        var result = await RunAsync(new SaveNenContractReflectionCommand { ContractId = contract.Id, Reflection = "  Went well  " });

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<NenContract>(contract.Id))!.Reflection.Text.Should().Be("Went well");
    }
}
