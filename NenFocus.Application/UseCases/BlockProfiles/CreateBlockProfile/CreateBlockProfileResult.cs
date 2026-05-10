using System;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed record CreateBlockProfileResult
{
    public bool Success { get; init; }
    public Guid? ProfileId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateBlockProfileResult Ok(Guid id) =>
        new() { Success = true, ProfileId = id };
    public static CreateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}