using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;

public sealed record DuplicateBlockProfileResult : ICommandResult<DuplicateBlockProfileResult>
{
    public bool Success { get; init; }
    public Guid? NewProfileId { get; init; }
    public string? ErrorMessage { get; init; }

    public static DuplicateBlockProfileResult Ok(Guid id) =>
        new() { Success = true, NewProfileId = id };
    public static DuplicateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
