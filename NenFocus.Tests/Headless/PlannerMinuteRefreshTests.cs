using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.ViewModels.Pages;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Today's floor badge counts up while a session runs, because a running block's credit grows with
/// time. The Planner re-reads the week's contracts on the display clock's minute tick — no timer of
/// its own (Gap 89 Slice A; it used to poll every 3 s). Credit is whole minutes, so a minute is as
/// often as the number can change.
/// </summary>
public class PlannerMinuteRefreshTests
{
    [AvaloniaFact]
    public async Task EachMinuteTick_ReReadsTheWeeksContracts()
    {
        var reads = 0;
        var queries = new FakeQueryDispatcher().Answer<GetNenContractsForDateRangeQuery, IReadOnlyList<NenContractRangeViewDto>>(_ =>
        {
            reads++;
            return [];
        });
        var displayClock = Substitute.For<IDisplayClock>();
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc));
        _ = new PlannerPageViewModel(queries, Substitute.For<ICommandDispatcher>(), displayClock,
            Substitute.For<IActiveSessionState>(), Substitute.For<IRecapRelay>(), systemClock);
        await SettleAsync();
        var readsAfterLoading = reads;

        displayClock.MinuteTicked += Raise.Event();
        await SettleAsync();

        reads.Should().BeGreaterThan(readsAfterLoading);
    }

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            Settle();
            await Task.Delay(20);
        }
    }
}
