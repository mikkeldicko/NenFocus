using FluentAssertions;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

/// <summary>Deleting a block preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class DeleteBlockPresetTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    public DeleteBlockPresetTests() => Clock.SetNow(Now);

    private static BlockPreset Preset() =>
        BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now));

    private Task<DeleteBlockPresetResult> DeleteAsync(Guid presetId) =>
        RunAsync(new DeleteBlockPresetCommand { PresetId = presetId });

    [Fact]
    public async Task NotInUse_IsRemoved()
    {
        var preset = Preset();
        await SeedAsync(preset);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<BlockPreset>(preset.Id)).Should().BeNull();
    }

    [Fact]
    public async Task AlreadyGone_Succeeds()
    {
        // Gap 110: removing is idempotent — a double click or a stale card is not an error.
        var result = await DeleteAsync(Guid.NewGuid());

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task InUseByTheRunningSession_IsRefused_AndKept()
    {
        var preset = Preset();
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            preset.Id, SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-5)));
        await SeedAsync(preset, session);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<BlockPreset>(preset.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task UsedByAFutureBlock_IsRefused_AndKept()
    {
        var preset = Preset();
        await SeedAsync(preset, FocusBlock.Create(Guid.NewGuid(), preset.Id, At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1))));

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<BlockPreset>(preset.Id)).Should().NotBeNull();
    }

    // ── Gap 89 Slice D: the block's own end decides, and an open deficit keeps the preset ──

    [Fact]
    public async Task UsedOnlyByABlockWhoseSessionEndedEarly_IsRemoved()
    {
        // 11:00–13:00, its session ended at 11:45: the block holds the preset only until then
        // (it used to stay locked until 13:00).
        var preset = Preset();
        var block = TestBlocks.Worked(FocusBlock.Create(Guid.NewGuid(), preset.Id, At.Slot(Now.AddHours(-1), Now.AddHours(1))),
            ritualAfterStart: 0, endedAfterStart: 45);
        await SeedAsync(preset, block);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task OwedByAContractInEffect_IsRefused_AndKept()
    {
        // Yesterday's Committed block was missed; Cover Deficit needs this preset to pay it back.
        var preset = Preset();
        var today = DateOnly.FromDateTime(Now);
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(), name: "Focused Week", goal: new NenContractGoal("Ship it"), strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(today.AddDays(-2), today.AddDays(4), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(), allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.AddDays(-3)), timeZone: At.UtcZone);
        var missed = FocusBlock.Create(Guid.NewGuid(), preset.Id, At.Slot(Now.AddDays(-1), Now.AddDays(-1).AddHours(1)),
            membership: TestContracts.Committed(contract.Id));
        await SeedAsync(preset, contract, missed);

        var result = await DeleteAsync(preset.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<BlockPreset>(preset.Id)).Should().NotBeNull();
    }
}
