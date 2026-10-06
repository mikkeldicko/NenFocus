using NenFocus.Domain.Entities;

namespace NenFocus.Tests;

/// <summary>
/// A block's stamps for tests, in the order production writes them: StartSession stamps the
/// Ritual, EndSession stamps the session's end. The block answers outcome and credit from these
/// alone (Gap 95), so tests set them instead of mocking a classifier or a session.
/// </summary>
internal static class TestBlocks
{
    /// <summary>
    /// The Ritual completed <paramref name="ritualAfterStart"/> minutes into the block; the session
    /// ended <paramref name="endedAfterStart"/> minutes into it, or is still running (null).
    /// </summary>
    public static FocusBlock Worked(FocusBlock block, double ritualAfterStart, double? endedAfterStart)
    {
        var start = At.Start(block.Scheduled);
        block.MarkRitualCompleted(start + TimeSpan.FromMinutes(ritualAfterStart));
        if (endedAfterStart is { } ended)
            block.MarkSessionEnded(start + TimeSpan.FromMinutes(ended));
        return block;
    }
}
