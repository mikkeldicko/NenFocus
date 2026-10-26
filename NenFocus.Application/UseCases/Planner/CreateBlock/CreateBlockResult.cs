using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CreateBlock;

public sealed record CreateBlockResult : ICommandResult<CreateBlockResult>
{
    public bool Success { get; init; }
    public Guid? BlockId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateBlockResult Ok(Guid id) => new() { Success = true, BlockId = id };
    public static CreateBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
