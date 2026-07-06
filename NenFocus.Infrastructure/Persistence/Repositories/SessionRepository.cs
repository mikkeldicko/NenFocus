using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private readonly NenFocusDbContext _context;

    public SessionRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .FirstOrDefaultAsync(
                s => s.Status == SessionStatus.Active ||
                     s.Status == SessionStatus.OnBreak,
                cancellationToken);
    }

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetHistoryAsync(
        DateOnly from,
        DateOnly to,
        Guid? presetId = null,
        CancellationToken cancellationToken = default)
    {
        var fromDate = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDate = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var query = _context.Sessions
            .Where(s => s.StartedAt >= fromDate && s.StartedAt <= toDate);

        if (presetId.HasValue)
            query = query.Where(s => s.BlockPresetId == presetId.Value);

        return await query
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .AnyAsync(
                s => s.Status == SessionStatus.Active ||
                     s.Status == SessionStatus.OnBreak,
                cancellationToken);
    }

    public async Task SaveAsync(Session session, CancellationToken cancellationToken = default)
    {
        var exists = await _context.Sessions
            .AnyAsync(s => s.Id == session.Id, cancellationToken);

        if (exists)
            _context.Sessions.Update(session);
        else
            await _context.Sessions.AddAsync(session, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        return await _context.Sessions
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.OnBreak)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsForOriginatingBlockAsync(Guid focusBlockId, CancellationToken ct = default)
    {
        return await _context.Sessions
            .AnyAsync(s => s.OriginatingBlockId == focusBlockId, ct);
    }
}
