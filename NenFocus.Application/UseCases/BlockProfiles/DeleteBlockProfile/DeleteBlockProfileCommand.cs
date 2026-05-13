using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed record DeleteBlockProfileCommand : ICommand<DeleteBlockProfileResult>
{
    public Guid ProfileId { get; init; }
}