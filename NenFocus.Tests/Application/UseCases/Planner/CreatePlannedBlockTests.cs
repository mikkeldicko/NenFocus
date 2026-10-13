using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// Creating a planned block, run as the app runs it: real pipeline, real SQLite (Gap 110).
/// Placement (past, contract hours, free for Focus) is GetBlockPlacementVerdictQuery's answer
/// (Gap 88 phase 8) — its every case is in GetBlockPlacementVerdictQueryHandlerTests; here, that a
/// refusal reaches the command and nothing is stored. The block's membership comes from where the
/// slot sits (Gap 97) — always Bonus: only Cover Deficit makes a redemption block (Gap 106).
/// </summary>
public class CreatePlannedBlockTests : UseCaseTestBase
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    public CreatePlannedBlockTests() => Clock.SetNow(Start.AddHours(-1));

    private static CreatePlannedBlockCommand FocusCommand(DateTime? start = null, DateTime? end = null) => new()
    {
        PresetId = SeededFocusPresetId,
        BlockPresetType = BlockPresetType.Focus,
        Slot = At.Slot(start ?? Start, end ?? End)
    };

    private static CreatePlannedBlockCommand BackgroundCommand(DateTime? start = null, DateTime? end = null) =>
        FocusCommand(start, end) with { PresetId = SeededBackgroundPresetId, BlockPresetType = BlockPresetType.Background };

    private Task<CreatePlannedBlockResult> CreateAsync(CreatePlannedBlockCommand command) => RunAsync(command);

    private async Task<PlannedBlock> CreatedAsync(CreatePlannedBlockCommand command)
    {
        var result = await CreateAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (await FindAsync<PlannedBlock>(result.BlockId!.Value))!;
    }

    private async Task ShouldBeRefused(CreatePlannedBlockCommand command, string? message = null)
    {
        var before = await ReadAsync(context => context.Set<PlannedBlock>().CountAsync());

        var result = await CreateAsync(command);

        result.Success.Should().BeFalse();
        if (message is not null) result.ErrorMessage.Should().Be(message);
        (await ReadAsync(context => context.Set<PlannedBlock>().CountAsync())).Should().Be(before);
    }

    /// <summary>A contract in effect whose period (the whole of 2–9 January) contains the command's slot.</summary>
    private async Task<Guid> ContractAroundTheSlot()
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone);
        await SeedAsync(contract);
        return contract.Id;
    }

    // ── What gets stored ─────────────────────────────────────────────────────

    [Fact]
    public async Task AFocusCommand_StoresAFocusBlock_AtTheRequestedSlot()
    {
        var saved = await CreatedAsync(FocusCommand());

        saved.Should().BeOfType<FocusBlock>();
        saved.BlockPresetId.Should().Be(SeededFocusPresetId);
        saved.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task ABackgroundCommand_StoresABackgroundBlock()
    {
        var saved = await CreatedAsync(BackgroundCommand());

        saved.Should().BeOfType<BackgroundBlock>();
    }

    [Fact]
    public async Task InsideNoContract_IsAPlainBlock()
    {
        var saved = await CreatedAsync(FocusCommand());

        saved.Membership.Should().BeNull();
    }

    [Fact]
    public async Task AFocusBlockInsideAContract_IsABonusBlockHoldingNoReceipt()
    {
        var contractId = await ContractAroundTheSlot();

        var saved = await CreatedAsync(FocusCommand());

        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.Membership.Should().Be(TestContracts.Bonus(contractId));
        focusBlock.Receipt.Should().Be(RedemptionReceipt.None);
        focusBlock.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task ABackgroundBlockInsideAContract_IsABonusBlock()
    {
        var contractId = await ContractAroundTheSlot();

        var saved = await CreatedAsync(BackgroundCommand());

        saved.Membership.Should().Be(TestContracts.Bonus(contractId));
    }

    // ── Form ─────────────────────────────────────────────────────────────────

    [Fact]
    public Task AnUnknownPreset_IsRefused() =>
        ShouldBeRefused(FocusCommand() with { PresetId = Guid.NewGuid() }, "Preset not found.");

    [Fact]
    public Task AnEmptyPresetId_IsRefused() =>
        ShouldBeRefused(FocusCommand() with { PresetId = Guid.Empty });

    [Fact]
    public Task APresetOfTheOtherType_IsRefused() =>
        ShouldBeRefused(FocusCommand() with { PresetId = SeededBackgroundPresetId },
            "A focus block requires a focus preset.");

    [Fact]
    public Task ShorterThanOneMinute_IsRefused() =>
        ShouldBeRefused(FocusCommand(Start, Start.AddSeconds(30)), "Block must be at least 1 minute long.");

    [Fact]
    public async Task ExactlyOneMinute_IsAllowed() =>
        (await CreatedAsync(FocusCommand(Start, Start.AddMinutes(1)))).Should().NotBeNull();

    [Fact]
    public Task AFocusBlockOverItsMaxDuration_IsRefused() =>
        ShouldBeRefused(FocusCommand(Start, Start.AddHours(FocusBlock.MaxDurationHours).AddMinutes(1)),
            $"A Focus block cannot exceed {FocusBlock.MaxDurationHours} hours.");

    [Fact]
    public async Task AFocusBlockAtExactlyItsMaxDuration_IsAllowed() =>
        (await CreatedAsync(FocusCommand(Start, Start.AddHours(FocusBlock.MaxDurationHours)))).Should().NotBeNull();

    [Fact]
    public async Task ABackgroundBlockLongerThanTheFocusMax_IsAllowed() =>
        (await CreatedAsync(BackgroundCommand(Start, Start.AddHours(FocusBlock.MaxDurationHours).AddDays(1)))).Should().NotBeNull();

    // ── Placement: the query's refusal reaches the command ──────────────────

    [Fact]
    public Task InThePast_IsRefused() =>
        ShouldBeRefused(FocusCommand(Start.AddHours(-3), Start.AddHours(-2)), "Start time must be in the future.");

    [Fact]
    public async Task OverAnotherFocusBlock_IsRefused()
    {
        await SeedAsync(FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Start.AddMinutes(30), End.AddMinutes(30))));

        await ShouldBeRefused(FocusCommand(), "A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task ABackgroundBlockOverAFocusBlock_IsAllowed()
    {
        await SeedAsync(FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Start, End)));

        (await CreatedAsync(BackgroundCommand())).Should().NotBeNull();
    }
}
