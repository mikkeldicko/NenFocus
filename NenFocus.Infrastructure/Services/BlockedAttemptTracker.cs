using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Infrastructure.Services;

public sealed class BlockedAttemptTracker : IBlockedAttemptTracker
{
    private readonly IDbContextFactory<NenFocusDbContext> _dbFactory;
    private readonly ISystemClock _clock;

    // Serializes the debounce check-then-insert below. Attempts are reported from both
    // the 2-second process-kill loop and the browser extension's /report endpoint, which
    // can run concurrently in-process; without this, two calls landing inside the same
    // debounce window could both read "no recent attempt" before either writes.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public BlockedAttemptTracker(IDbContextFactory<NenFocusDbContext> dbFactory, ISystemClock clock)
    {
        _dbFactory = dbFactory;
        _clock = clock;
    }

    public async Task RecordAppBlockedAsync(Guid sessionId, string processName)
    {
        await SaveAttemptInternal(BlockedAttempt.Record(Guid.NewGuid(), sessionId, processName, BlockedAttemptType.App, _clock.Now));
    }

    public async Task RecordWebBlockedAsync(Guid sessionId, string domain)
    {
        await SaveAttemptInternal(BlockedAttempt.Record(Guid.NewGuid(), sessionId, domain, BlockedAttemptType.Website, _clock.Now));
    }

    private async Task SaveAttemptInternal(BlockedAttempt attempt)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            var threshold = _clock.Now - TimeSpan.FromSeconds(2);

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
        finally
        {
            _writeLock.Release();
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