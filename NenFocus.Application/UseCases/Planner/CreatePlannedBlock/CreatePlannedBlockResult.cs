using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockResult : ICommandResult<CreatePlannedBlockResult>
{
    public bool Success { get; init; }
    public Guid? BlockId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreatePlannedBlockResult Ok(Guid id) => new() { Success = true, BlockId = id };
    public static CreatePlannedBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
