
using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface ISessionStateService
{
    SessionDto? ActiveSession { get; }
    bool IsRitualRequired { get; }
    Guid? PendingFocusBlockId { get; }

    event EventHandler<SessionDto?>? SessionChanged;
    event EventHandler<RitualRequirementChange>? RitualRequirementChanged;

    /// <summary>
    /// Raised when a background process (the enforcement heartbeat's periodic missed-block
    /// sweep) creates one or more new Post-Session records nobody was watching for live —
    /// as opposed to SessionChanged/RitualRequirementChanged, which react to state this
    /// service itself already owns. Never raised with an empty list.
    /// </summary>
    event EventHandler<IReadOnlyList<PostSessionDto>>? PostSessionsReady;

    /// <summary>Raised once a pending Post-Session overlay is dismissed — signals other views (e.g. the Planner) that resolved-block state may have changed.</summary>
    event EventHandler? PostSessionAcknowledged;

    /// <summary>
    /// Raised when NenContractHeartbeatService's periodic sweep passively completes one or
    /// more Nen Contracts nobody was watching for live — mirrors PostSessionsReady exactly,
    /// for the same reason (a background process, not a direct user action, produced this).
    /// A contract Broken via the Planner's own button is a direct synchronous UI action and
    /// never raises this — its recap flows straight from the Break command's own result
    /// instead. Never raised with an empty list.
    /// </summary>
    event EventHandler<IReadOnlyList<NenContractRecapDto>>? PostContractsReady;

    void SetActive(SessionDto session);
    void SetRitualRequired(bool required, Guid? focusBlockId = null);
    void NotifyPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions);
    void NotifyPostSessionAcknowledged();
    void NotifyPostContractsReady(IReadOnlyList<NenContractRecapDto> recaps);
    void Clear();
    Task InitializeAsync();
}