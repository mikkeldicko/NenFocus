
using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface ISessionStateService
{
    SessionDto? ActiveSession { get; }
    bool IsRitualRequired { get; }

    event EventHandler<SessionDto?>? SessionChanged;
    event EventHandler<bool>? RitualRequirementChanged;

    void SetActive(SessionDto session);
    void SetRitualRequired(bool required);
    void Clear();
    Task InitializeAsync();
}