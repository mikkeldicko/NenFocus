using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Infrastructure.Services;

public sealed class BlockedAttemptTracker : IBlockedAttemptTracker
{
    private readonly IDbContextFactory<NenFocusDbContext> _dbFactory;

    public BlockedAttemptTracker(IDbContextFactory<NenFocusDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task RecordAppBlockedAsync(Guid sessionId, string processName)
    {
        await SaveAttemptInternal(BlockedAttempt.Record(Guid.NewGuid(), sessionId, processName, BlockedAttemptType.App, DateTime.UtcNow));
    }

    public async Task RecordWebBlockedAsync(Guid sessionId, string domain)
    {
        await SaveAttemptInternal(BlockedAttempt.Record(Guid.NewGuid(), sessionId, domain, BlockedAttemptType.Website, DateTime.UtcNow));
    }

    private async Task SaveAttemptInternal(BlockedAttempt attempt)
    {
        using var context = await _dbFactory.CreateDbContextAsync();

        var threshold = DateTime.UtcNow.AddSeconds(-2);

        // SQLite can't apply the OccurredAt comparison in-query, so fetch the most
        // recent row only and compare in C#.
        var latest = await context.BlockedAttempts
            .Where(a => a.SessionId == attempt.SessionId &&
                        a.ResourceName == attempt.ResourceName)
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync();

        bool alreadyLoggedRecently = latest is not null && latest.OccurredAt > threshold;

        if (!alreadyLoggedRecently)
        {
            context.BlockedAttempts.Add(attempt);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<BlockedAttempt>> GetAttemptsForSessionAsync(Guid sessionId)
    {
        using var context = await _dbFactory.CreateDbContextAsync();

        var attempts = await context.BlockedAttempts
            .Where(a => a.SessionId == sessionId)
            .ToListAsync();

        return attempts
            .OrderByDescending(a => a.OccurredAt)
            .ToList();
    }
}