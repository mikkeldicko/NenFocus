using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;

public sealed record DuplicateBlockProfileCommand : ICommand<DuplicateBlockProfileResult>
{
    public Guid SourceProfileId { get; init; }
    public string? NewName { get; init; }
}