using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockResult : ICommandResult<CreatePlannedBlockResult>
{
    public bool Success { get; init; }
    public Guid? BlockId { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the requested End was shortened because it exceeded the deficit this
    /// redemption block covers — see FocusBlock.CapEndToRedemptionCoverage. Always false
    /// for a non-redemption block.
    /// </summary>
    public bool RedemptionCapped { get; init; }

    public static CreatePlannedBlockResult Ok(Guid id, bool redemptionCapped = false) =>
        new() { Success = true, BlockId = id, RedemptionCapped = redemptionCapped };
    public static CreatePlannedBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
