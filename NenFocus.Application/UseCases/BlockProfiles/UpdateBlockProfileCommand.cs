using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed record UpdateBlockProfileCommand
{
    public Guid ProfileId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsitePolicy WebsitePolicy { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}

public sealed record UpdateBlockProfileResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateBlockProfileResult Ok() => new() { Success = true };
    public static UpdateBlockProfileResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class UpdateBlockProfileCommandHandler
    : ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;

    public UpdateBlockProfileCommandHandler(IBlockProfileRepository profiles)
        => _profiles = profiles;

    public async Task<UpdateBlockProfileResult> HandleAsync(
        UpdateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdateBlockProfileResult.Fail($"Profile '{command.ProfileId}' not found.");

        if (await _profiles.ExistsWithNameAsync(command.Name, command.ProfileId, cancellationToken))
            return UpdateBlockProfileResult.Fail($"A profile named '{command.Name}' already exists.");

        // Rename if changed
        if (profile.Name != command.Name)
            profile.Rename(command.Name);

        // Update website policy (this clears existing websites per domain logic)
        if (profile.WebsitePolicy != command.WebsitePolicy)
            profile.ChangeWebsitePolicy(command.WebsitePolicy);

        // Reconcile apps: remove old, add new
        foreach (var existing in profile.BlockedApps.ToList())
            profile.RemoveBlockedApp(existing);
        foreach (var appName in command.BlockedApps)
            profile.AddBlockedApp(new BlockedApp(appName));

        // Reconcile websites: remove old, add new
        foreach (var existing in profile.Websites.ToList())
            profile.RemoveWebsite(existing);
        foreach (var url in command.Websites)
            profile.AddWebsite(new BlockedSite(url));

        // Reconcile tags: remove old, add new
        foreach (var existing in profile.Tags.ToList())
            profile.RemoveTag(existing);
        foreach (var tag in command.Tags)
            profile.AddTag(tag);

        await _profiles.UpdateAsync(profile, cancellationToken);

        return UpdateBlockProfileResult.Ok();
    }
}