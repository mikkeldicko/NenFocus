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
        await SaveAttemptInternal(new BlockedAttempt(sessionId, processName, BlockedAttemptType.App));
    }

    public async Task RecordWebBlockedAsync(Guid sessionId, string domain)
    {
        await SaveAttemptInternal(new BlockedAttempt(sessionId, domain, BlockedAttemptType.Website));
    }

    private async Task SaveAttemptInternal(BlockedAttempt attempt)
    {
        using var context = await _dbFactory.CreateDbContextAsync();

        var threshold = DateTimeOffset.Now.AddSeconds(-2);

        // 1. Fetch only the items for this session and resource from the DB.
        // We leave the time comparison out of the 'await' to avoid the SQLite error.
        var recentAttempts = await context.BlockedAttempts
            .Where(a => a.SessionId == attempt.SessionId &&
                        a.ResourceName == attempt.ResourceName)
            .ToListAsync();

        // 2. Perform the time check in C# memory
        bool alreadyLoggedRecently = recentAttempts.Any(a => a.Timestamp > threshold);

        if (!alreadyLoggedRecently)
        {
            context.BlockedAttempts.Add(attempt);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<BlockedAttempt>> GetAttemptsForSessionAsync(Guid sessionId)
    {
        using var context = await _dbFactory.CreateDbContextAsync();

        // 1. Fetch the data from the DB first without sorting
        var attempts = await context.BlockedAttempts
            .Where(a => a.SessionId == sessionId)
            .ToListAsync();

        // 2. Sort the list in memory using C# logic
        return attempts
            .OrderByDescending(a => a.Timestamp)
            .ToList();
    }
}