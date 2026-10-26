namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

/// <summary>
/// The sweep has no payload now: it only stamps missed blocks (Gap 114, D5). Their recap is read
/// from the block by a later phase, not carried here.
/// </summary>
public sealed record SweepMissedFocusBlocksResult;
