
using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface ISessionStateService
{
    SessionDto? ActiveSession { get; }
    bool IsRitualRequired { get; } // ADD THIS

    event EventHandler<SessionDto?>? SessionChanged;
    event EventHandler<bool>? RitualRequirementChanged; // ADD THIS

    void SetActive(SessionDto session);
    void SetRitualRequired(bool required); // ADD THIS
    void Clear();
    Task InitializeAsync();
}