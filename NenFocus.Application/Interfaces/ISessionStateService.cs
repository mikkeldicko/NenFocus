
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

    void SetActive(SessionDto session);
    void SetRitualRequired(bool required, Guid? focusBlockId = null);
    void NotifyPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions);
    void NotifyPostSessionAcknowledged();
    void Clear();
    Task InitializeAsync();
}