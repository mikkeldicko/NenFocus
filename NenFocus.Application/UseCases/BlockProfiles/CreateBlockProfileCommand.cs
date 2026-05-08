using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed record CreateBlockProfileCommand
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsitePolicy WebsitePolicy { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}

public sealed record CreateBlockProfileResult
{
    public bool Success { get; init; }
    public Guid? ProfileId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateBlockProfileResult Ok(Guid id) =>
        new() { Success = true, ProfileId = id };
    public static CreateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class CreateBlockProfileCommandHandler
    : ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;

    public CreateBlockProfileCommandHandler(IBlockProfileRepository profiles)
        => _profiles = profiles;

    public async Task<CreateBlockProfileResult> HandleAsync(
        CreateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _profiles.ExistsWithNameAsync(command.Name, cancellationToken: cancellationToken))
            return CreateBlockProfileResult.Fail($"A profile named '{command.Name}' already exists.");

        // Use your domain's Create method then add apps/sites/tags individually
        var profile = BlockProfile.Create(command.Name, command.WebsitePolicy, command.Tags);

        foreach (var appName in command.BlockedApps)
            profile.AddBlockedApp(new NenFocus.Domain.ValueObjects.BlockedApp(appName));

        foreach (var url in command.Websites)
            profile.AddWebsite(new NenFocus.Domain.ValueObjects.BlockedSite(url));

        await _profiles.AddAsync(profile, cancellationToken);

        return CreateBlockProfileResult.Ok(profile.Id);
    }
}