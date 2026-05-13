using NenFocus.Application.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;

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
            newName = $"{newName} ({DateTime.UtcNow:HH:mm})";

        var duplicate = source.Duplicate(newName, Guid.NewGuid());
        await _profiles.AddAsync(duplicate, cancellationToken);

        return DuplicateBlockProfileResult.Ok(duplicate.Id);
    }
}