namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed record DeleteBlockProfileResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteBlockProfileResult Ok() => new() { Success = true };
    public static DeleteBlockProfileResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}