using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>
/// Starting a session, run as the app runs it: real pipeline, real SQLite (Gap 110). The run must
/// be free Focus time (BlockPlacementChecker.CheckFree, Gap 88) — which is also "one session at a
/// time" (Gap 92); a planned session is its block's full length and ends with it; breaks come from
/// the chosen preset laid over the real run (Gap 63); keep-blocking is copied from the block's
/// contract (Gap 64). The form checks live in StartSessionValidatorTests until Gap 115.
/// </summary>
public class StartSessionTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public StartSessionTests() => Clock.SetNow(Now);

    private static StartSessionCommand SpontaneousCommand() => new()
    {
        Goal = "Deep work",
        PresetId = SeededFocusPresetId,
        DurationMinutes = 30
    };

    private static StartSessionCommand PlannedCommand(Block block) =>
        SpontaneousCommand() with { OriginatingBlockId = block.Id };

    private static FocusBlock Block(DateTime from, DateTime to, ContractMembership? membership = null) =>
        FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(from, to), membership: membership);

    private async Task<Session> StartedAsync(StartSessionCommand command)
    {
        var result = await RunAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (await FindAsync<Session>(result.SessionId!.Value))!;
    }

    private async Task<StartSessionResult> ShouldBeRefused(StartSessionCommand command)
    {
        var before = await ReadAsync(context => context.Sessions.CountAsync());

        var result = await RunAsync(command);

        result.Success.Should().BeFalse();
        (await ReadAsync(context => context.Sessions.CountAsync())).Should().Be(before);
        return result;
    }

    private async Task<FocusBlock> StoredBlock(Block block) => (FocusBlock)(await FindAsync<Block>(block.Id))!;

    // ── Spontaneous ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ASpontaneousSession_IsStoredActive()
    {
        var session = await StartedAsync(SpontaneousCommand());

        session.Origin.Should().Be(SessionOrigin.Spontaneous);
        session.Status.Should().Be(SessionStatus.Active);
        session.StartedAt.Should().Be(At.Utc(Now));
    }

    // ── A session's own extra apps and sites (Gap 112) ────────────────────────

    [Fact]
    public async Task ExtraAppsAndSites_AreStoredAsTheAppAndSiteTheyAre()
    {
        var session = await StartedAsync(SpontaneousCommand() with
        {
            AdditionalBlockedApps = [@"C:\Games\Steam.exe", "steam"],
            AdditionalBlockedSites = ["https://www.Reddit.com/r/all"]
        });

        session.TemporaryBlockedApps.Should().Equal(new BlockedApp("steam"));
        session.TemporaryBlockedSites.Should().Equal(new Website("reddit.com"));
    }

    [Fact]
    public async Task ACriticalProcessAsAnExtraApp_IsRefused_SayingWhy()
    {
        // StartSession used to let this through; only the enforcement service's own skip stopped it.
        var result = await ShouldBeRefused(SpontaneousCommand() with { AdditionalBlockedApps = [@"C:\Windows\explorer.exe"] });

        result.ErrorMessage.Should().Contain("critical system process");
    }

    [Fact]
    public async Task AnExtraSiteThatIsNoWebsite_IsRefused()
    {
        var result = await ShouldBeRefused(SpontaneousCommand() with { AdditionalBlockedSites = ["https://"] });

        result.ErrorMessage.Should().Contain("is not a website");
    }

    [Fact]
    public async Task AFocusBlockStartingWithinTheDuration_IsRefused_SayingWhen()
    {
        await SeedAsync(Block(Now.AddMinutes(10), Now.AddHours(1)));

        var result = await ShouldBeRefused(SpontaneousCommand());

        result.ErrorMessage.Should().Contain("10 minutes");
    }

    [Fact]
    public async Task ASessionAlreadyRunning_IsRefused()
    {
        // One session at a time is "Focus time never overlaps" (Gap 92, C+): the running session
        // holds its time, and a session starting now always lands inside it.
        var running = Session.Create(Guid.NewGuid(), new SessionGoal("Already focusing"),
            new SessionDuration(TimeSpan.FromHours(1)), SeededFocusPresetId, SessionOrigin.Spontaneous);
        running.Start(At.Utc(Now.AddMinutes(-10)));
        await SeedAsync(running);

        var result = await ShouldBeRefused(SpontaneousCommand());

        result.ErrorMessage.Should().Contain("already running");
    }

    [Fact]
    public async Task AFocusBlockAlreadyActive_IsRefused()
    {
        await SeedAsync(Block(Now.AddMinutes(-5), Now.AddHours(1)));

        await ShouldBeRefused(SpontaneousCommand());
    }

    [Fact]
    public async Task TheFreedTailOfABlockThatEndedEarly_IsFree()
    {
        // A block whose session ended early no longer holds the rest of its window.
        var endedEarly = Block(Now.AddHours(-2), Now.AddHours(1));
        endedEarly.MarkRitualCompleted(At.Utc(Now.AddHours(-2)));
        endedEarly.MarkSessionEnded(At.Utc(Now.AddMinutes(-5)));
        await SeedAsync(endedEarly);

        await StartedAsync(SpontaneousCommand());
    }

    // ── Breaks (Gap 63) ──────────────────────────────────────────────────────

    private static readonly BreakTimer Rhythm = new(25, 5, 4, 15);

    private Task<Guid> RhythmPresetAsync() => BreakTimerPresetAsync("Test rhythm", Rhythm);

    [Fact]
    public async Task WithABreakTimerPreset_StoresTheResultingScheduleAndThePresetsName()
    {
        var presetId = await RhythmPresetAsync();

        var session = await StartedAsync(SpontaneousCommand() with { BreakTimerPresetId = presetId, DurationMinutes = 60 });

        session.BreakSchedule.Should().Be(Rhythm.ScheduleFor(TimeSpan.FromMinutes(60)));
        session.BreakPresetName.Should().Be("Test rhythm");
    }

    [Fact]
    public async Task APresetWithNoRoomForABreak_StartsWithNoBreaksAndNoLabel()
    {
        // A preset can stop fitting between Hatsu and Start (the user waited) — the session still
        // starts, honestly labelled as having no breaks.
        var presetId = await RhythmPresetAsync();

        var session = await StartedAsync(SpontaneousCommand() with { BreakTimerPresetId = presetId, DurationMinutes = 29 });

        session.BreakSchedule.IsEmpty.Should().BeTrue();
        session.BreakPresetName.Should().BeNull();
    }

    [Fact]
    public async Task ALatePlannedStart_CountsBreaksFromTheActualStart()
    {
        // Block 11:50–12:50, Ritual done at 12:00: the first break is 25 minutes after the session
        // started (12:25), not after the block (12:15).
        var block = Block(Now.AddMinutes(-10), Now.AddMinutes(50));
        await SeedAsync(block);
        var presetId = await RhythmPresetAsync();

        var session = await StartedAsync(PlannedCommand(block) with { BreakTimerPresetId = presetId });

        session.BreakSchedule.Breaks.Single().Start.Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public Task AnUnknownBreakTimerPreset_IsRefused() =>
        ShouldBeRefused(SpontaneousCommand() with { BreakTimerPresetId = Guid.NewGuid() });

    // ── Planned ──────────────────────────────────────────────────────────────

    // ── Length: one rule, a Focus period is at most FocusBlock.MaxLength (Gap 89 Slice B, A1) ──

    [Fact]
    public async Task ABlockLongerThanEightHours_StartsItsSession()
    {
        // Hatsu sends what is left of the block. The validator used to hold every session to
        // 480 minutes, so a 10-hour block — which the Planner allows — could never start.
        var block = Block(Now.AddMinutes(-1), Now.AddHours(10).AddMinutes(-1));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block) with { DurationMinutes = 599 });

        session.EndAt.Should().Be(At.Utc(Now.AddHours(10).AddMinutes(-1)));
    }

    [Fact]
    public async Task ASpontaneousSession_MayRunAsLongAsAFocusBlock()
    {
        var session = await StartedAsync(SpontaneousCommand() with { DurationMinutes = (int)FocusBlock.MaxLength.TotalMinutes });

        session.EndAt.Should().Be(At.Utc(Now.Add(FocusBlock.MaxLength)));
    }

    [Fact]
    public async Task ASpontaneousSessionLongerThanAFocusBlock_IsRefused()
        => await ShouldBeRefused(SpontaneousCommand() with { DurationMinutes = (int)FocusBlock.MaxLength.TotalMinutes + 1 });

    [Fact]
    public async Task APlannedSession_StampsItsBlocksRitual()
    {
        var block = Block(Now.AddMinutes(-1), Now.AddHours(1));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block));

        session.Origin.Should().Be(SessionOrigin.Planned);
        session.OriginatingBlockId.Should().Be(block.Id);
        (await StoredBlock(block)).Outcome(At.Utc(Now)).Should().Be(FocusBlockOutcome.CompletedOnTime); // a minute in: within grace
    }

    [Fact]
    public async Task APlannedSession_EndsWithItsBlock_NotWhenItsDurationSays()
    {
        // However much wizard time passed before Start (what DurationMinutes reflects).
        var block = Block(Now.AddMinutes(-2), Now.AddMinutes(3));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block) with { DurationMinutes = 5 });

        session.EndAt.Should().Be(At.Utc(Now.AddMinutes(3)));
    }

    [Fact]
    public async Task APlannedSessionsDuration_IsTheBlocksFullLength()
    {
        // Never "what's left" nor the caller's stale DurationMinutes — this keeps the credited
        // duration at End always ≤ Planned.
        var block = Block(Now.AddMinutes(-2), Now.AddMinutes(58)); // the Ritual took 2 minutes
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block) with { DurationMinutes = 45 }); // stale: neither 60 nor 58

        session.Duration.Value.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task AShortBlock_WhoseRitualTookHalfOfIt_StillPlansItsFullLength()
    {
        // A 2-minute block with a 1-minute Ritual: "Planned" is 2 minutes, not the 1 remaining.
        var block = Block(Now.AddMinutes(-1), Now.AddMinutes(1));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block));

        session.Duration.Value.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public Task AnUnknownBlock_IsRefused() =>
        ShouldBeRefused(PlannedCommand(Block(Now.AddMinutes(-1), Now.AddHours(1))));

    [Fact]
    public async Task ItsOwnBlock_IsNotInTheWay()
    {
        var block = Block(Now.AddMinutes(-5), Now.AddMinutes(55));
        await SeedAsync(block);

        await StartedAsync(PlannedCommand(block));

        (await StoredBlock(block)).Outcome(At.Utc(Now)).Should().Be(FocusBlockOutcome.CompletedOnTime); // 5 minutes in: within grace
    }

    [Fact]
    public async Task ABlockWhoseWindowHasClosed_IsRefused()
    {
        var block = Block(Now.AddHours(-2), Now.AddHours(-1));
        await SeedAsync(block);

        await ShouldBeRefused(PlannedCommand(block));
    }

    [Fact]
    public async Task ABlockEndingExactlyNow_IsRefused()
    {
        // Block.HasEnded is half-open (Gap 88): at exactly End the block has ended.
        var block = Block(Now.AddHours(-1), Now);
        await SeedAsync(block);

        await ShouldBeRefused(PlannedCommand(block));
    }

    [Fact]
    public async Task ABlockThatAlreadyHasASession_IsRefused()
    {
        var block = Block(Now.AddMinutes(-1), Now.AddHours(1));
        await SeedAsync(block, TestSessions.For(block.Id));

        await ShouldBeRefused(PlannedCommand(block));

        (await StoredBlock(block)).Outcome(At.Utc(Now)).Should().NotBe(FocusBlockOutcome.CompletedOnTime); // no Ritual stamped
    }

    // ── Keep blocking during breaks (Gap 64) ─────────────────────────────────

    private async Task<NenContract> ContractAsync(bool keepBlockingDuringBreaks)
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone,
            keepBlockingDuringBreaks: keepBlockingDuringBreaks);
        await SeedAsync(contract);
        return contract;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ABlockInAContract_CopiesTheContractsRuleOntoTheSession(bool keepBlocking)
    {
        var contract = await ContractAsync(keepBlocking);
        var block = Block(Now, Now.AddHours(1), TestContracts.Committed(contract.Id));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block));

        session.KeepBlockingDuringBreaks.Should().Be(keepBlocking);
    }

    [Fact]
    public async Task ABonusBlockOfAContract_FollowsTheContractsRuleToo()
    {
        // Gap 97: the rule applies to every member's session, not only committed blocks.
        var contract = await ContractAsync(keepBlockingDuringBreaks: true);
        var block = Block(Now, Now.AddHours(1), TestContracts.Bonus(contract.Id));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block));

        session.KeepBlockingDuringBreaks.Should().BeTrue();
    }

    [Fact]
    public async Task APlainBlock_DoesNotKeepBlocking()
    {
        var block = Block(Now, Now.AddHours(1));
        await SeedAsync(block);

        var session = await StartedAsync(PlannedCommand(block));

        session.KeepBlockingDuringBreaks.Should().BeFalse();
    }

    [Fact]
    public async Task ASpontaneousSession_DoesNotKeepBlocking()
    {
        var session = await StartedAsync(SpontaneousCommand());

        session.KeepBlockingDuringBreaks.Should().BeFalse();
    }
}
