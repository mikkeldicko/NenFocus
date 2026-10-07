using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CoverDeficit;

public sealed record CoverDeficitResult : ICommandResult<CoverDeficitResult>
{
    public bool Success { get; init; }
    public Guid? BlockId { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>The tile was shortened to what's owed by enough to tell the user (RedemptionVerdict.WasCapped).</summary>
    public bool WasCapped { get; init; }

    public static CoverDeficitResult Ok(Guid blockId, bool wasCapped) => new() { Success = true, BlockId = blockId, WasCapped = wasCapped };
    public static CoverDeficitResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
