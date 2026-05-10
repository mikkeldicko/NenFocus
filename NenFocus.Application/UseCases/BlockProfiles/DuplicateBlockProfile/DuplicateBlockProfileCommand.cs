using System;

namespace NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;

public sealed record DuplicateBlockProfileCommand
{
    public Guid SourceProfileId { get; init; }
    public string? NewName { get; init; }
}