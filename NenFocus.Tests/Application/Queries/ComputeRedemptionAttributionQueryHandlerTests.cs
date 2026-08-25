using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputeRedemptionAttributionQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public ComputeRedemptionAttributionQueryHandlerTests()
    {
        // Identity conversion by default — every existing scenario below reasons purely in
        // UTC, so "local" trivially equals the UTC value unless a test overrides it.
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
    }

    private ComputeRedemptionAttributionQueryHandler CreateHandler() => new(_contracts, _deficits, _clock);

    private static NenContract SameDayContract(Guid id) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"), Today.AddDays(-1), Today.AddDays(7),
            StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), Now);

    private static NenContract AnyDayContract(Guid id) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"), Today.AddDays(-7), Today.AddDays(7),
            StrictnessRule.PeriodTimeBudget, DailyFloorPolicy.None, null, false,
            new Dictionary<DateOnly, double>(), Now, contractFloor: new ContractFloor(10));

    private static FocusBlockDeficitDto Deficit(Guid presetId, DateTime scheduledStart, double remaining, Guid? focusBlockId = null) =>
        new()
        {
            FocusBlockId = focusBlockId ?? Guid.NewGuid(),
            BlockPresetId = presetId,
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledStart.AddHours(1),
            Outcome = FocusBlockOutcome.Missed,
            RemainingDeficitMinutes = remaining,
            PlannedMinutes = 60,
            CompletedMinutes = 0
        };

    [Fact]
    public async Task HandleAsync_AutoDiscover_NoInEffectContracts_ReturnsNone()
    {
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, Guid.NewGuid(), Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().BeNull();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_AutoDiscover_EligibleSameDayDeficit_AttachesToThatContract()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().Be(contractId);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId && e.MinutesCovered == 40);
    }

    [Fact]
    public async Task HandleAsync_DifferentPreset_ExcludedFromEligibility()
    {
        var contractId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(Guid.NewGuid(), Now, remaining: 40);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, Guid.NewGuid(), Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().BeNull();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_SameDayContract_MissedOnADifferentDay_ExcludedAsWrongDay()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now.AddDays(-1), remaining: 40); // missed yesterday
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().BeNull();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UsesLocalCalendarDay_NotRawUtcDate_ForRedemptionEligibility()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);

        // Missed block scheduled 1:00 AM UTC Jan 2 — but 8:00 PM local (UTC-5) on Jan 1.
        var missedScheduledUtc = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc);
        var deficit = Deficit(presetId, missedScheduledUtc, remaining: 40);

        // Proposed block at 11:00 PM UTC Jan 1 — 6:00 PM local (UTC-5), the *same* local day
        // as the miss even though their raw UTC dates differ. A fix that forgot to convert
        // would compare Jan 2 (UTC) against Jan 1 (UTC) and wrongly report WrongDay.
        var proposedStartUtc = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);

        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>().AddHours(-5));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, proposedStartUtc, proposedStartUtc.AddHours(1), null),
            CancellationToken.None);

        result.ContractId.Should().Be(contractId);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId);
    }

    [Fact]
    public async Task HandleAsync_ExcludeBlockId_RemovesThatCandidate()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId, ExcludeCoverageFromBlockId: deficit.FocusBlockId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddHours(1), deficit.FocusBlockId), CancellationToken.None);

        result.ContractId.Should().BeNull();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_EditingAnAlreadyCoveringBlock_ExcludesItsOwnStaleEntryFromCoverageSum()
    {
        // Reproduces the live bug: editing an already-covering redemption block used to see
        // its own stale, pre-edit RedemptionEntries (still in the DB until the edit itself
        // is saved) as "already covered by someone else" — GetNenContractDeficitsQuery must
        // be asked to exclude the block being edited from that sum, or the recompute
        // collapses toward zero on every edit.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var editingBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId, ExcludeCoverageFromBlockId: editingBlockId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddMinutes(8), editingBlockId), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId && e.MinutesCovered == 8);
    }

    [Fact]
    public async Task HandleAsync_TwoDeficitsSamePreset_OldestFirstWithSpillover()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var older = Deficit(presetId, Now.AddHours(-3), remaining: 30);
        var newer = Deficit(presetId, Now.AddHours(-1), remaining: 50);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[newer, older]); // deliberately out of order

        // A 100-minute block: fully covers the 30-min older deficit, spills 50 into the
        // newer one, and the remaining 20 minutes of surplus attributes to nothing further.
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddMinutes(100), null), CancellationToken.None);

        result.ContractId.Should().Be(contractId);
        result.Entries.Should().HaveCount(2);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == older.FocusBlockId && e.MinutesCovered == 30);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == newer.FocusBlockId && e.MinutesCovered == 50);
    }

    [Fact]
    public async Task HandleAsync_MultipleInEffectContracts_OnlyOneHasAnEligibleDeficit_ResolvesToIt()
    {
        var presetId = Guid.NewGuid();
        var eligibleContractId = Guid.NewGuid();
        var otherContractId = Guid.NewGuid();
        var eligibleContract = SameDayContract(eligibleContractId);
        var otherContract = SameDayContract(otherContractId);
        var deficit = Deficit(presetId, Now, remaining: 25);

        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[otherContract, eligibleContract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(otherContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(eligibleContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().Be(eligibleContractId);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId);
    }

    [Fact]
    public async Task HandleAsync_ExplicitContractId_NoEligibleDeficit_EchoesContractIdBack()
    {
        // Editing a block that's already tied to a contract but doesn't happen to cover
        // anything right now must not silently lose its contract association.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().Be(contractId);
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ExplicitContractId_ScopesSearchToJustThatContract()
    {
        // Even though another in-effect contract would have an eligible match, an explicit
        // ContractId must never search outside of it.
        var scopedContractId = Guid.NewGuid();
        var otherContractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var scopedContract = SameDayContract(scopedContractId);
        var otherDeficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(scopedContractId, Arg.Any<CancellationToken>()).Returns(scopedContract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(scopedContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(otherContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[otherDeficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(scopedContractId, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().Be(scopedContractId);
        result.Entries.Should().BeEmpty();
        await _contracts.DidNotReceive().GetAllInEffectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetWithContractFloor_MissedOnADifferentDay_StillEligible()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = AnyDayContract(contractId);
        var deficit = Deficit(presetId, Now.AddDays(-3), remaining: 40);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(null, presetId, Now, Now.AddHours(1), null), CancellationToken.None);

        result.ContractId.Should().Be(contractId);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId);
    }
}
