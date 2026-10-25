using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Interim (Gap 92, A3): the post-session and post-contract relays, moved out of the old
/// ISessionStateService unchanged. Gap 107 decides their real owner — one Application owner of
/// "what the shell shows next" — and whether they should come from the Domain events that
/// already exist (SessionMissedEvent, NenContractCompletedEvent) instead.
/// </summary>
public interface IRecapRelay
{
    /// <summary>
    /// Raised when a background process (the enforcement heartbeat's missed-block sweep) creates
    /// Post-Session records nobody was watching for live. Never raised with an empty list.
    /// </summary>
    event EventHandler<IReadOnlyList<PostSessionDto>>? PostSessionsReady;

    /// <summary>Raised once a Post-Session overlay is dismissed — resolved-block state may have changed (e.g. the Planner reloads).</summary>
    event EventHandler? PostSessionAcknowledged;

    /// <summary>
    /// Raised when a Nen Contract ends — completed by the heartbeat's sweep or by the user, or broken —
    /// by NenContractEndedEventHandler after the save, the one path to the recap (Gap 110). Never
    /// raised with an empty list.
    /// </summary>
    event EventHandler<IReadOnlyList<NenContractRecapDto>>? PostContractsReady;

    void NotifyPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions);
    void NotifyPostSessionAcknowledged();
    void NotifyPostContractsReady(IReadOnlyList<NenContractRecapDto> recaps);
}
