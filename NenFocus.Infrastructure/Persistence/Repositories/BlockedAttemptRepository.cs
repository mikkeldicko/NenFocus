using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class BlockedAttemptRepository : IBlockedAttemptRepository
{
    private readonly NenFocusDbContext _context;

    public BlockedAttemptRepository(NenFocusDbContext context)
        => _context = context;

    public void Add(BlockedAttempt attempt) => _context.BlockedAttempts.Add(attempt);

    public async Task<BlockedAttempt?> GetLatestAsync(Guid sessionId, string resourceName, CancellationToken ct = default)
    {
        // Ordering by the stored moment translates; comparing it to a threshold doesn't — so the
        // latest row comes back and BlockedAttempt.IsRepeatOf compares in C#.
        return await _context.BlockedAttempts
            .Where(a => a.SessionId == sessionId && a.ResourceName == resourceName)
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BlockedAttempt>> GetForSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var attempts = await _context.BlockedAttempts
            .Where(a => a.SessionId == sessionId)
            .ToListAsync(ct);

        return attempts.OrderByDescending(a => a.OccurredAt).ToList();
    }
}
