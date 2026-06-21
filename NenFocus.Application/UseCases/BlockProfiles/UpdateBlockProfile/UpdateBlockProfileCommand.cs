using NenFocus.Domain.Enums;
using System;
using System.Collections.Generic;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed record UpdateBlockProfileCommand : ICommand<UpdateBlockProfileResult>
{
    public Guid ProfileId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsitePolicy WebsitePolicy { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}
