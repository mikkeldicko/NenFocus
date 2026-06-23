using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Interfaces;

public interface ISessionRepository
{
    Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetHistoryAsync(
        DateOnly from,
        DateOnly to,
        Guid? profileId = null,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Session session, CancellationToken cancellationToken = default);

    Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default);
}
