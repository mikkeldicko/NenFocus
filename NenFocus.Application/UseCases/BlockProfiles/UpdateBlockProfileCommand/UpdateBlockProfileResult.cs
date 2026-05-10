namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed record UpdateBlockProfileResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateBlockProfileResult Ok() => new() { Success = true };
    public static UpdateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}