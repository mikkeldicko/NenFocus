namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdatePlannedBlockResult Ok() => new() { Success = true };
    public static UpdatePlannedBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
