using NenFocus.Domain.Enums;
using System.Collections.Generic;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed record CreateBlockProfileCommand
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsitePolicy WebsitePolicy { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}