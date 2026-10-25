using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 110: a repository is a collection — changes to a loaded aggregate are saved by the commit,
/// with no update or save call. Each test saves an aggregate, loads it through a second context
/// (as a command's own scope would), changes it only through its Domain methods, commits with
/// <c>SaveChangesAsync</c> alone, and reloads through a third — so nothing is read back from a
/// change tracker that already holds it.
/// </summary>
public class ChangeTrackingRoundTripTests : SqliteRepositoryTestBase
{
    private static readonly DateTime Day = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
    private static Moment Now => At.Utc(Day.AddHours(8));

    private async Task SeedAsync(params object[] aggregates)
    {
        Context.AddRange(aggregates);
        await Context.SaveChangesAsync();
    }

    /// <summary>Loads through a fresh context, applies <paramref name="change"/>, commits with nothing else.</summary>
    private async Task ChangeAndCommitAsync<T>(Func<NenFocusDbContext, Task<T?>> load, Action<T> change) where T : class
    {
        await using var scope = CreateFreshContext();
        var loaded = await load(scope);
        loaded.Should().NotBeNull();
        change(loaded!);
        await scope.SaveChangesAsync();
    }

    private async Task<T> ReloadAsync<T>(Func<NenFocusDbContext, Task<T?>> load) where T : class
    {
        await using var fresh = CreateFreshContext();
        var reloaded = await load(fresh);
        reloaded.Should().NotBeNull();
        return reloaded!;
    }

    // ── Block presets: scalars and two owned lists ───────────────────────────

    private static BlockPreset NewBlockPreset()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now);
        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddBlockedApp(new BlockedApp("steam"));
        preset.AddWebsite(new Website("youtube.com"));
        preset.AddWebsite(new Website("reddit.com"));
        return preset;
    }

    private static Func<NenFocusDbContext, Task<BlockPreset?>> BlockPresetById(Guid id) =>
        ctx => new BlockPresetRepository(ctx).GetByIdAsync(id);

    [Fact]
    public async Task BlockPreset_RenameRetypeAndListChanges_AreSavedByTheCommit()
    {
        var preset = NewBlockPreset();
        await SeedAsync(preset);

        await ChangeAndCommitAsync(BlockPresetById(preset.Id), p =>
        {
            p.Rename("Renamed");
            p.ChangePresetType(BlockPresetType.Background);
            p.AddBlockedApp(new BlockedApp("spotify"));
            p.RemoveBlockedApp(new BlockedApp("steam"));
            p.AddWebsite(new Website("x.com"));
            p.RemoveWebsite(new Website("reddit.com"));
        });

        var reloaded = await ReloadAsync(BlockPresetById(preset.Id));
        reloaded.Name.Should().Be("Renamed");
        reloaded.PresetType.Should().Be(BlockPresetType.Background);
        reloaded.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["chrome", "spotify"]);
        reloaded.Websites.Select(s => s.Url).Should().BeEquivalentTo(["youtube.com", "x.com"]);
    }

    [Fact]
    public async Task BlockPreset_ChangingTheFilterType_ClearsItsSites_AndTheCommitSavesThat()
    {
        var preset = NewBlockPreset();
        await SeedAsync(preset);

        await ChangeAndCommitAsync(BlockPresetById(preset.Id), p =>
        {
            p.ChangeWebsiteFilterType(WebsiteFilterType.Allowlist);
            p.AddWebsite(new Website("github.com"));
        });

        var reloaded = await ReloadAsync(BlockPresetById(preset.Id));
        reloaded.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        reloaded.Websites.Select(s => s.Url).Should().Equal("github.com");
    }

    // ── Break timer and contract presets: a replaced owned value ───────────────────────

    [Fact]
    public async Task BreakTimerPreset_Update_IsSavedByTheCommit()
    {
        var preset = BreakTimerPreset.Create(Guid.NewGuid(), "Mine", new BreakTimer(25, 5, 4, 15));
        await SeedAsync(preset);

        await ChangeAndCommitAsync(ctx => new BreakTimerPresetRepository(ctx).GetByIdAsync(preset.Id),
            p => p.Update("Mine, longer", new BreakTimer(50, 10, 3, 20)));

        var reloaded = await ReloadAsync(ctx => new BreakTimerPresetRepository(ctx).GetByIdAsync(preset.Id));
        reloaded.Name.Should().Be("Mine, longer");
        reloaded.BreakTimer.Should().Be(new BreakTimer(50, 10, 3, 20));
    }

    [Fact]
    public async Task ContractPreset_Update_RulesAndWorkHours_IsSavedByTheCommit()
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Mine", TestRules.Of(), workHours: null);
        await SeedAsync(preset);
        var rules = TestRules.Of(StrictnessRule.DailyFlexibility, new DailyFloor(3), allowCancellation: true,
            keepBlockingDuringBreaks: true, RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true);
        var hours = new WorkHours(new TimeOnly(8, 0), new TimeOnly(16, 0));

        await ChangeAndCommitAsync(ctx => new ContractPresetRepository(ctx).GetByIdAsync(preset.Id),
            p => p.Update("Mine, edited", rules, hours));

        var reloaded = await ReloadAsync(ctx => new ContractPresetRepository(ctx).GetByIdAsync(preset.Id));
        reloaded.Name.Should().Be("Mine, edited");
        reloaded.Rules.Should().Be(rules);
        reloaded.WorkHours.Should().Be(hours);
    }

    [Fact]
    public async Task ContractPreset_RemovingWorkHours_IsSavedByTheCommit()
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Mine", TestRules.Of(), new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        await SeedAsync(preset);

        await ChangeAndCommitAsync(ctx => new ContractPresetRepository(ctx).GetByIdAsync(preset.Id),
            p => p.Update("Mine", TestRules.Of(), workHours: null));

        var reloaded = await ReloadAsync(ctx => new ContractPresetRepository(ctx).GetByIdAsync(preset.Id));
        reloaded.WorkHours.Should().BeNull();
    }

    // ── Nen contracts: status, the JSON calendar, reflection ───────────────────────────

    private static NenContract NewContract(bool allowCancellation = true) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day).AddDays(6), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: allowCancellation,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(Day.AddDays(-1)),
        timeZone: At.UtcZone);

    private static Func<NenFocusDbContext, Task<NenContract?>> ContractById(Guid id) =>
        ctx => new NenContractRepository(ctx).GetByIdAsync(id);

    [Fact]
    public async Task NenContract_Break_ShortensTheCalendar_AndTheCommitSavesIt()
    {
        var contract = NewContract();
        contract.Activate(At.Utc(Day));
        await SeedAsync(contract);
        var brokenAt = At.Utc(Day.AddDays(2).AddHours(10));

        await ChangeAndCommitAsync(ContractById(contract.Id), c => c.Break(brokenAt, At.UtcZone, sessionRunning: false));

        var reloaded = await ReloadAsync(ContractById(contract.Id));
        reloaded.Status.Should().Be(NenContractStatus.Broken);
        reloaded.EndDate.Should().Be(DateOnly.FromDateTime(Day).AddDays(2));
        reloaded.WindowOn(DateOnly.FromDateTime(Day).AddDays(3)).Should().BeNull("the days after the break day are gone");
        reloaded.OriginalEndDate.Should().Be(DateOnly.FromDateTime(Day).AddDays(6));
    }

    [Fact]
    public async Task NenContract_ActivateCompleteReflectAcknowledge_AreSavedByTheCommit()
    {
        var contract = NewContract();
        await SeedAsync(contract);

        await ChangeAndCommitAsync(ContractById(contract.Id), c =>
        {
            c.Activate(At.Utc(Day));
            c.Complete(At.Utc(Day.AddDays(7)));
            c.SetReflection("Went well");
            c.MarkPostContractAcknowledged(At.Utc(Day.AddDays(7).AddHours(1)));
        });

        var reloaded = await ReloadAsync(ContractById(contract.Id));
        reloaded.Status.Should().Be(NenContractStatus.Completed);
        reloaded.Reflection.Should().Be("Went well");
        reloaded.PostContractAcknowledgedAt.Should().Be(At.Utc(Day.AddDays(7).AddHours(1)));
    }

    // ── Planned blocks: the owned slot, membership, stamps ─────────────────────────────

    private static TimeSlot SlotAt(int hour, int hours = 1) => At.Slot(Day.AddHours(hour), Day.AddHours(hour + hours));

    private static Func<NenFocusDbContext, Task<PlannedBlock?>> BlockById(Guid id) =>
        ctx => new PlannedBlockRepository(ctx).GetByIdAsync(id);

    [Fact]
    public async Task PlannedBlock_RescheduleAndNewPreset_AreSavedByTheCommit()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), SlotAt(10));
        await SeedAsync(block);
        var newPreset = Guid.NewGuid();

        await ChangeAndCommitAsync(BlockById(block.Id), b =>
        {
            b.Reschedule(SlotAt(13, 2));
            b.AssignPreset(newPreset);
        });

        var reloaded = await ReloadAsync(BlockById(block.Id));
        reloaded.Scheduled.Should().Be(SlotAt(13, 2));
        reloaded.BlockPresetId.Should().Be(newPreset);
    }

    [Fact]
    public async Task PlannedBlock_LeavingItsContract_AndCancelling_AreSavedByTheCommit()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), SlotAt(10), membership: TestContracts.Bonus(Guid.NewGuid()));
        await SeedAsync(block);

        await ChangeAndCommitAsync(BlockById(block.Id), b =>
        {
            b.Leave();
            b.MarkCancelled(Now);
        });

        var reloaded = await ReloadAsync(BlockById(block.Id));
        reloaded.Membership.Should().BeNull();
        reloaded.CancelledAt.Should().Be(Now);
    }

    [Fact]
    public async Task FocusBlock_RitualAndSessionStamps_AreSavedByTheCommit()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), SlotAt(10));
        await SeedAsync(block);

        await ChangeAndCommitAsync(BlockById(block.Id), b =>
        {
            var focus = (FocusBlock)b;
            focus.MarkRitualCompleted(At.Utc(Day.AddHours(10).AddMinutes(5)));
            focus.MarkSessionEnded(At.Utc(Day.AddHours(10).AddMinutes(40)));
        });

        var reloaded = (FocusBlock)await ReloadAsync(BlockById(block.Id));
        reloaded.Outcome(At.Utc(Day.AddHours(12))).Should().Be(FocusBlockOutcome.EndedEarly);
    }

    // ── Sessions: status, times, the owned break list ──────────────────────────────────

    private static Session RunningSessionWithBreaks()
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(2)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Day.AddHours(9)), breakSchedule: new BreakSchedule([
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)),
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(60)),
            new SessionBreak(SessionPhaseKind.LongBreak, TimeSpan.FromMinutes(85), TimeSpan.FromMinutes(100))]),
            breakPresetName: "Classic");
        return session;
    }

    private static Func<NenFocusDbContext, Task<Session?>> SessionById(Guid id) =>
        ctx => new SessionRepository(ctx).GetByIdAsync(id);

    [Fact]
    public async Task Session_Complete_TrimsTheBreakList_AndTheCommitSavesIt()
    {
        var session = RunningSessionWithBreaks();
        await SeedAsync(session);

        await ChangeAndCommitAsync(SessionById(session.Id), s => s.Complete(At.Utc(Day.AddHours(9).AddMinutes(40)), null));

        var reloaded = await ReloadAsync(SessionById(session.Id));
        reloaded.Status.Should().Be(SessionStatus.Completed);
        reloaded.EndedAt.Should().Be(At.Utc(Day.AddHours(9).AddMinutes(40)));
        reloaded.BreakSchedule.Breaks.Should().HaveCount(1, "only the first break had started by 40 minutes in");
    }

    [Fact]
    public async Task Session_ReflectionAndAcknowledgement_AreSavedByTheCommit()
    {
        var session = RunningSessionWithBreaks();
        session.Complete(At.Utc(Day.AddHours(11)), null);
        await SeedAsync(session);

        await ChangeAndCommitAsync(SessionById(session.Id), s =>
        {
            s.SetReflection("Good one");
            s.MarkPostSessionAcknowledged(At.Utc(Day.AddHours(11).AddMinutes(1)));
        });

        var reloaded = await ReloadAsync(SessionById(session.Id));
        reloaded.Reflection.Should().Be("Good one");
        reloaded.PostSessionAcknowledgedAt.Should().Be(At.Utc(Day.AddHours(11).AddMinutes(1)));
        reloaded.BreakSchedule.Breaks.Should().HaveCount(3);
    }

    // ── The hardest caller: EditNenContract removes a contract and adds a new one, same id ─

    [Fact]
    public async Task RemovingAContractAndAddingANewOneWithTheSameId_InOneCommit_ReplacesIt()
    {
        var original = NewContract(allowCancellation: false);
        var oldBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), SlotAt(10), membership: TestContracts.Committed(original.Id));
        await SeedAsync(original, oldBlock);

        var replacement = TestContracts.Seal(
            id: original.Id,
            name: "Edited",
            goal: new NenContractGoal("New goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day).AddDays(3), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Day.AddDays(-1)),
            timeZone: At.UtcZone);
        var newBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), SlotAt(14), membership: TestContracts.Committed(original.Id));

        await using (var scope = CreateFreshContext())
        {
            var contracts = new NenContractRepository(scope);
            var blocks = new PlannedBlockRepository(scope);
            foreach (var b in await blocks.GetForContractAsync(original.Id))
                blocks.Remove(b);
            contracts.Remove((await contracts.GetByIdAsync(original.Id))!);
            blocks.Add(newBlock);
            contracts.Add(replacement);
            await scope.SaveChangesAsync();
        }

        var reloaded = await ReloadAsync(ContractById(original.Id));
        reloaded.Name.Should().Be("Edited");
        reloaded.EndDate.Should().Be(DateOnly.FromDateTime(Day).AddDays(3));
        await using var fresh = CreateFreshContext();
        (await new PlannedBlockRepository(fresh).GetForContractAsync(original.Id)).Select(b => b.Id).Should().Equal(newBlock.Id);
    }
}
