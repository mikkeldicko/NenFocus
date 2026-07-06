using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed record SweepMissedFocusBlocksCommand : ICommand<SweepMissedFocusBlocksResult>;
