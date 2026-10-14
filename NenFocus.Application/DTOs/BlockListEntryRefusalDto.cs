using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Why an app or site can't go on a block list, worded once — for a preset (Create, Update) and for a
/// session's own extras (StartSession) alike (Gap 112). The reasons are the Domain's
/// (<c>BlockedApp.Check</c>, <c>Website.TryCreate</c>); Gap 100 moves the sentences to a resource file.
/// </summary>
public sealed record BlockListEntryRefusalDto
{
    public string Message { get; init; } = string.Empty;

    public static BlockListEntryRefusalDto ForApp(string entry, BlockedAppRefusal reason) => new()
    {
        Message = reason switch
        {
            BlockedAppRefusal.CriticalProcess => $"'{entry}' is a critical system process and cannot be blocked.",
            _ => "An app needs a name.",
        }
    };

    public static BlockListEntryRefusalDto ForWebsite(string entry) => new()
    {
        Message = $"'{entry}' is not a website — enter a domain like youtube.com.",
    };
}
