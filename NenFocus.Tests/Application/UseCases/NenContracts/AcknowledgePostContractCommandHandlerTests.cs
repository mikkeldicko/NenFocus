using FluentAssertions;
using NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>Acknowledging a contract's recap, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class AcknowledgePostContractCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);

    public AcknowledgePostContractCommandHandlerTests() => Clock.SetNow(Now);

    private static NenContract ActiveContract() => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private Task<AcknowledgePostContractResult> AcknowledgeAsync(Guid contractId) =>
        RunAsync(new AcknowledgePostContractCommand { ContractId = contractId });

    [Fact]
    public async Task ContractNotFound_Fails()
    {
        var result = await AcknowledgeAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CompletedContract_IsStampedAcknowledged()
    {
        var contract = ActiveContract();
        contract.Complete(At.Utc(Now));
        await SeedAsync(contract);

        var result = await AcknowledgeAsync(contract.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<NenContract>(contract.Id))!.PostContractAcknowledgedAt.Should().Be(At.Utc(Now));
    }

    [Fact]
    public async Task StillActiveContract_IsRefused_AndNotStamped()
    {
        var contract = ActiveContract();
        await SeedAsync(contract);

        var result = await AcknowledgeAsync(contract.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<NenContract>(contract.Id))!.PostContractAcknowledgedAt.Should().BeNull();
    }
}
