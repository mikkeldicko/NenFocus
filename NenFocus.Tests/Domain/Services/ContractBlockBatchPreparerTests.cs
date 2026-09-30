using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.Domain.Services;

public class ContractBlockBatchPreparerTests
{
    // Local = UTC, so these cases read as before; the local-day bug fix has its own test below.
    private static readonly UserTimeZone UtcZone = new(TimeZoneInfo.Utc);

    // The real calculator, not a mock — this is pure domain-to-domain composition, and
    // using the real implementation is what makes these tests meaningful without mocks,
    // the same reason the preparer itself was extracted.
    private readonly ContractBlockBatchPreparer _preparer = new(new PlannedFocusHoursCalculator());

    private static readonly Guid PresetId = Guid.NewGuid();
    private static readonly Guid ContractId = Guid.NewGuid();

    [Fact]
    public void Prepare_NoSpecs_SucceedsWithEmptyBatch()
    {
        var result = _preparer.Prepare([], ContractId, UtcZone);

        result.Success.Should().BeTrue();
        result.Blocks.Should().BeEmpty();
        result.Blocks.OfType<FocusBlock>().Should().BeEmpty();
        result.PlannedFocusHoursByDay.Should().BeEmpty();
    }

    [Fact]
    public void Prepare_FocusSpec_CreatesFocusBlockLinkedToContract()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var end = new DateTime(2026, 1, 1, 11, 0, 0);

        var result = _preparer.Prepare([(PresetId, At.Slot(start, end), true, "Deep Work")], ContractId, UtcZone);

        result.Success.Should().BeTrue();
        result.Blocks.Should().ContainSingle().Which.Should().BeOfType<FocusBlock>();
        result.Blocks[0].ContractId.Should().Be(ContractId);
        result.Blocks[0].Scheduled.Should().Be(At.Slot(start, end));
        result.Blocks.OfType<FocusBlock>().Should().ContainSingle();
    }

    [Fact]
    public void Prepare_BackgroundSpec_CreatesBackgroundBlockNotCountedAsFocus()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var end = new DateTime(2026, 1, 1, 11, 0, 0);

        var result = _preparer.Prepare([(PresetId, At.Slot(start, end), false, null)], ContractId, UtcZone);

        result.Success.Should().BeTrue();
        result.Blocks.Should().ContainSingle().Which.Should().BeOfType<BackgroundBlock>();
        result.Blocks.OfType<FocusBlock>().Should().BeEmpty();
    }

    [Fact]
    public void Prepare_NoContractId_BlocksHaveNullContractId()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var end = new DateTime(2026, 1, 1, 11, 0, 0);

        var result = _preparer.Prepare([(PresetId, At.Slot(start, end), true, null)], contractId: null, UtcZone);

        result.Blocks[0].ContractId.Should().BeNull();
    }

    [Fact]
    public void Prepare_OverlappingFocusSpecs_Fails()
    {
        var first = (PresetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), true, (string?)null);
        var overlapping = (PresetId, At.Slot(new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0)), true, (string?)null);

        var result = _preparer.Prepare([first, overlapping], ContractId, UtcZone);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(ContractBlockBatchOutcome.OverlapsWithinBatch);
    }

    [Fact]
    public void Prepare_NonOverlappingFocusSpecs_Succeeds()
    {
        var first = (PresetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), true, (string?)null);
        var second = (PresetId, At.Slot(new DateTime(2026, 1, 1, 11, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0)), true, (string?)null);

        var result = _preparer.Prepare([first, second], ContractId, UtcZone);

        result.Success.Should().BeTrue();
        result.Blocks.OfType<FocusBlock>().Should().HaveCount(2);
    }

    [Fact]
    public void Prepare_FocusAndBackgroundOverlapping_DoesNotFail()
    {
        // Only Focus+Focus overlap is a violation — Background blocks are unconstrained.
        var focus = (PresetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), true, (string?)null);
        var background = (PresetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), false, (string?)null);

        var result = _preparer.Prepare([focus, background], ContractId, UtcZone);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Prepare_FocusSpecs_ComputesPlannedFocusHoursByDayFromFocusOnly()
    {
        var day = new DateOnly(2026, 1, 1);
        var focus = (PresetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), true, (string?)null);
        var background = (PresetId, At.Slot(new DateTime(2026, 1, 1, 12, 0, 0), new DateTime(2026, 1, 1, 15, 0, 0)), false, (string?)null);

        var result = _preparer.Prepare([focus, background], ContractId, UtcZone);

        result.PlannedFocusHoursByDay.Should().ContainKey(day).WhoseValue.Should().Be(2);
    }

    [Fact]
    public void Prepare_BlockJustAfterLocalMidnight_CountsOnTheUsersDay()
    {
        // Gap 88 phase 4 bug fix: Seal/Edit used to count planned hours per UTC day. In
        // Copenhagen, 00:30–01:30 on 2 Jan is 23:30–00:30 UTC — it must count on 2 Jan only.
        var copenhagen = new UserTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
        var focus = (PresetId, At.Slot(new DateTime(2026, 1, 1, 23, 30, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 30, 0, DateTimeKind.Utc)), true, (string?)null);

        var result = _preparer.Prepare([focus], ContractId, copenhagen);

        result.PlannedFocusHoursByDay.Should().Equal(new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 2)] = 1 });
    }
}