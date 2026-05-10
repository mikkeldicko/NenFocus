using System;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed record DeleteBlockProfileCommand
{
    public Guid ProfileId { get; init; }
}