
using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface ISessionStateService
{
    SessionDto? ActiveSession { get; }
    bool IsRitualRequired { get; }
    Guid? PendingFocusBlockId { get; }

    event EventHandler<SessionDto?>? SessionChanged;
    event EventHandler<RitualRequirementChange>? RitualRequirementChanged;
    event EventHandler<IReadOnlyList<PostSessionDto>>? MissedSessionsDiscovered;

    void SetActive(SessionDto session);
    void SetRitualRequired(bool required, Guid? focusBlockId = null);
    void Clear();
    Task InitializeAsync();
}