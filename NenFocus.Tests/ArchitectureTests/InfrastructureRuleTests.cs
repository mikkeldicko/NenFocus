using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Infrastructure is the outside world and holds no rules (MODULE_STANDARDS → Layer Responsibilities;
/// Gap 112). It asks the Domain's answers — ActivePolicySlice.Blocks, BlockedAppFor — and reports what it
/// saw; it never re-decides from the pieces those answers are made of.
/// </summary>
public class InfrastructureRuleTests
{
    // The allowlist/blocklist mode and the separate allowlists are what Blocks is worked out from
    // (I2); the session id is what an attempt's owner is worked out from, in RecordBlockedAttempt (I3).
    private static readonly Regex DecidesFromThePolicysParts = new(@"\.\s*(WebsiteFilterTypeMode|AllowLists|ActiveSessionId)\b");

    [Fact]
    public void InfrastructureNeverDecidesFromThePolicysParts()
    {
        var violations = SourceCode.Files("NenFocus.Infrastructure")
            .Where(f => DecidesFromThePolicysParts.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }

    // A call that reaches outside the app: the database, the OS, the network, time passing, hosting,
    // the machine clock, or a DI scope (composition — the dispatchers and the event publisher).
    private static readonly Regex OutsideWorldCall = new(
        @"\b(DbContext|DbUpdateException|SqliteException|Process\s*\.|Registry\s*\.|HttpListener|X509\w*|" +
        @"System\.Timers|Timer\b|BackgroundService|CreateScope|CreateAsyncScope|ToastNotification\w*|AppNotification\w*|" +
        @"DateTime(Offset)?\s*\.\s*(UtcNow|Now))");

    /// <summary>
    /// A service in Infrastructure that touches nothing outside the app is an Application service in
    /// the wrong layer (I4 moved five). Looks for real calls, not imports (Gap 112, C5).
    /// </summary>
    [Fact]
    public void EveryInfrastructureServiceTouchesTheOutsideWorld()
    {
        var violations = SourceCode.Files("NenFocus.Infrastructure/Services")
            .Where(f => !OutsideWorldCall.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }
}
