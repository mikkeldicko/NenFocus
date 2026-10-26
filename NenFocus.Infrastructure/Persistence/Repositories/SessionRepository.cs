using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
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
                s => s.Status == SessionStatus.Active,
                cancellationToken);
    }

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetHistoryAsync(
        TimeSlot startedWithin,
        CancellationToken cancellationToken = default)
    {
        // "Started inside the slot" is the Domain's own filter, so the SQL can't drift from
        // TimeSlot.Contains; which days the slot covers is the caller's, through UserTimeZone
        // (Gap 97 — this used to read its dates as UTC days). StartedAt is Domain-only, so it's
        // reached by name.
        var query = _context.Sessions
            .Where(SlotQuery.OnMoment<Session>(s => EF.Property<Moment?>(s, "StartedAt"), TimeSlot.Filters.Contains(startedWithin)));

        return await query
            .OrderByDescending(s => EF.Property<Moment?>(s, "StartedAt"))
            .ToListAsync(cancellationToken);
    }

    public async Task<Session?> GetByOriginatingBlockIdAsync(Guid focusBlockId, CancellationToken ct = default)
    {
        return await _context.Sessions
            .FirstOrDefaultAsync(s => s.OriginatingBlockId == focusBlockId, ct);
    }

    public async Task<IReadOnlyList<Session>> GetUnacknowledgedPostSessionsAsync(CancellationToken ct = default)
    {
        return await _context.Sessions
            .Where(s =>
                s.Status == SessionStatus.Completed &&
                s.Reflection.AcknowledgedAt == null)
            .OrderBy(s => EF.Property<Moment?>(s, "EndedAt"))
            .ToListAsync(ct);
    }

    public void Add(Session session) => _context.Sessions.Add(session);
}
