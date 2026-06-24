using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed record CreateBlockProfileCommand : ICommand<CreateBlockProfileResult>
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsitePolicy WebsitePolicy { get; init; }
    public BlockType ProfileType { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}