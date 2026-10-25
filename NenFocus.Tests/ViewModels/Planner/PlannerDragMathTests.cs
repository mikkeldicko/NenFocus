using FluentAssertions;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Tests.ViewModels.Planner;

public class PlannerDragMathTests
{
    private const int FirstHour = 0;
    private static TimeSpan At(int hour, int minute = 0) => new(hour, minute, 0);

    // ── ClampMoveStart — a MOVE must never resize the tile ──────────────────────

    [Fact]
    public void ClampMoveStart_WithinBounds_ReturnsSnappedStartUnchanged()
        => PlannerDragMath.ClampMoveStart(At(20, 45), TimeSpan.FromMinutes(38), FirstHour)
            .Should().Be(At(20, 45));

    [Fact]
    public void ClampMoveStart_WouldPushEndPastMidnight_ShiftsStartEarlierPreservingDuration()
    {
        // A 38-minute block dropped at 23:50 would end at 00:28 — shift start back so the
        // exact same 38-minute duration fits before 24:00, instead of truncating it.
        var duration = TimeSpan.FromMinutes(38);
        var result = PlannerDragMath.ClampMoveStart(At(23, 50), duration, FirstHour);

        result.Should().Be(TimeSpan.FromHours(24) - duration);
        (result + duration).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void ClampMoveStart_BelowFirstHour_ClampsToFirstHour()
        => PlannerDragMath.ClampMoveStart(TimeSpan.FromMinutes(-15), TimeSpan.FromMinutes(30), FirstHour)
            .Should().Be(At(0));

    [Fact]
    public void ClampMoveStart_OddDurationNeverRounds()
    {
        // The regression this whole fix targets: a 38-minute block, moved anywhere legal,
        // must stay exactly 38 minutes — never silently rounded up to 45.
        var duration = TimeSpan.FromMinutes(38);
        var result = PlannerDragMath.ClampMoveStart(At(21, 0), duration, FirstHour);

        result.Should().Be(At(21, 0));
        (result + duration).Should().Be(At(21, 38));
    }

    // ── ClampResizeTopStart — anchorEnd (the untouched edge) must never move ────

    [Fact]
    public void ClampResizeTopStart_WithinBounds_ReturnsCandidateUnchanged()
        => PlannerDragMath.ClampResizeTopStart(At(20, 0), At(21, 1), FirstHour)
            .Should().Be(At(20, 0));

    [Fact]
    public void ClampResizeTopStart_TooCloseToAnchorEnd_ClampsToMinimumDuration()
        => PlannerDragMath.ClampResizeTopStart(At(20, 55), At(21, 1), FirstHour)
            .Should().Be(At(20, 46)); // anchorEnd (21:01) minus the 15-minute floor

    [Fact]
    public void ClampResizeTopStart_BelowFirstHour_ClampsToFirstHour()
        => PlannerDragMath.ClampResizeTopStart(TimeSpan.FromMinutes(-10), At(9), FirstHour)
            .Should().Be(At(0));

    [Fact]
    public void ClampResizeTopStart_NeverAltersAnchorEnd()
    {
        // anchorEnd is a caller-held value, never returned or mutated by this call — this
        // test simply documents the contract: the anchor is a pass-through, not adjusted.
        var anchorEnd = At(21, 1);
        PlannerDragMath.ClampResizeTopStart(At(19, 0), anchorEnd, FirstHour);
        anchorEnd.Should().Be(At(21, 1));
    }

    // ── ClampResizeBottomEnd — anchorStart (the untouched edge) must never move ─

    [Fact]
    public void ClampResizeBottomEnd_WithinBounds_ReturnsCandidateUnchanged()
        => PlannerDragMath.ClampResizeBottomEnd(At(21, 30), At(20, 23), FirstHour)
            .Should().Be(At(21, 30));

    [Fact]
    public void ClampResizeBottomEnd_TooCloseToAnchorStart_ClampsToMinimumDuration()
        => PlannerDragMath.ClampResizeBottomEnd(At(20, 25), At(20, 23), FirstHour)
            .Should().Be(At(20, 38)); // anchorStart (20:23) plus the 15-minute floor

    [Fact]
    public void ClampResizeBottomEnd_PastMidnight_ClampsToMidnight()
        => PlannerDragMath.ClampResizeBottomEnd(TimeSpan.FromHours(25), At(20, 0), FirstHour)
            .Should().Be(TimeSpan.FromHours(24));

    [Fact]
    public void ClampResizeBottomEnd_NeverAltersAnchorStart()
    {
        var anchorStart = At(20, 23);
        PlannerDragMath.ClampResizeBottomEnd(At(22, 0), anchorStart, FirstHour);
        anchorStart.Should().Be(At(20, 23));
    }
}
