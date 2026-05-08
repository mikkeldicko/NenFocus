using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;

public sealed record DuplicateBlockProfileCommand
{
    public Guid SourceProfileId { get; init; }
    public string? NewName { get; init; }
}

public sealed record DuplicateBlockProfileResult
{
    public bool Success { get; init; }
    public Guid? NewProfileId { get; init; }
    public string? ErrorMessage { get; init; }

    public static DuplicateBlockProfileResult Ok(Guid id) =>
        new() { Success = true, NewProfileId = id };
    public static DuplicateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class DuplicateBlockProfileCommandHandler
    : ICommandHandler<DuplicateBlockProfileCommand, DuplicateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;

    public DuplicateBlockProfileCommandHandler(IBlockProfileRepository profiles)
        => _profiles = profiles;

    public async Task<DuplicateBlockProfileResult> HandleAsync(
        DuplicateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await _profiles.GetByIdAsync(command.SourceProfileId, cancellationToken);
        if (source is null)
            return DuplicateBlockProfileResult.Fail($"Profile '{command.SourceProfileId}' not found.");

        var newName = command.NewName ?? $"Copy of {source.Name}";

        if (await _profiles.ExistsWithNameAsync(newName, cancellationToken: cancellationToken))
            newName = $"{newName} ({DateTime.Now:HH:mm})";

        var duplicate = source.Duplicate(newName);
        await _profiles.AddAsync(duplicate, cancellationToken);

        return DuplicateBlockProfileResult.Ok(duplicate.Id);
    }
}