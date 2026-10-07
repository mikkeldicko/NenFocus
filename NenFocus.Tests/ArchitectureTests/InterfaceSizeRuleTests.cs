using System.Reflection;
using NenFocus.Application.Services;
using NenFocus.Domain.Services;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// MODULE_STANDARDS → Interface-First checklist: 5 or fewer public members. A new interface is held
/// to it; an existing larger one is listed with today's count and may not grow — growth one "small
/// addition" at a time is how ISessionStateService went from 5 to 15 with nobody deciding it.
/// Entries are "Name has N": growing adds an unlisted entry, shrinking leaves a stale one, so the
/// count must be edited either way — adding a member becomes a visible decision.
/// This counts; it can't judge depth. Whether a big interface is justified stays a review job (Gap 92).
/// </summary>
public class InterfaceSizeRuleTests
{
    private const int MaxMembers = 5;

    private static readonly string[] KnownLargeInterfaces =
    [
        // Three jobs: active session, Ritual prompt, post-session/post-contract relays. → Gap 92 A3, Gap 89 area 4.
        "ISessionStateService has 15",

        // Repositories: in DDD a collection-like door to one aggregate. Each extra query method is
        // usually a business rule moving into SQL (e.g. HasFutureBlocksForPresetAsync, Gap 92 A6),
        // so these are capped too — a new method is a decision, not a habit.
        "IPlannedBlockRepository has 9",
        "INenContractRepository has 9",
        "ISessionRepository has 6",
        "IBlockPresetRepository has 7",
        "IBreakTimerPresetRepository has 6",
        "IContractPresetRepository has 6",
    ];

    private static int PublicMembers(Type i) =>
        i.GetMethods().Count(m => !m.IsSpecialName) + i.GetProperties().Length + i.GetEvents().Length;

    [Fact]
    public void InterfacesStaySmallOrDoNotGrow()
    {
        var violations = new[] { typeof(PolicyEngine), typeof(Notifier), typeof(SystemClock), typeof(ShellViewModel) }
            .SelectMany(t => t.Assembly.GetTypes())
            .Where(t => t.IsInterface && t.Namespace?.StartsWith("NenFocus", StringComparison.Ordinal) == true)
            .Select(i => (Name: i.Name, Count: PublicMembers(i)))
            .Where(x => x.Count > MaxMembers)
            .Select(x => $"{x.Name} has {x.Count}");

        KnownExceptions.Check(violations, KnownLargeInterfaces);
    }
}
