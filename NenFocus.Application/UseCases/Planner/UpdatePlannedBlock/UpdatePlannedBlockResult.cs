using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockResult : ICommandResult<UpdatePlannedBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the requested End was shortened because it exceeded the deficit this
    /// redemption block covers — see FocusBlock.CapEndToRedemptionCoverage. Always false
    /// for a non-redemption block.
    /// </summary>
    public bool RedemptionCapped { get; init; }

    public static UpdatePlannedBlockResult Ok(bool redemptionCapped = false) =>
        new() { Success = true, RedemptionCapped = redemptionCapped };
    public static UpdatePlannedBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
