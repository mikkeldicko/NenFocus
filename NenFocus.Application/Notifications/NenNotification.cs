using NenFocus.Domain.Enums;

namespace NenFocus.Application.Notifications;

/// <summary>
/// Something the user should be told, as a fact — never as wording. Sources report one of
/// these to <see cref="Interfaces.INotifier"/>, which alone decides the title and body. Each
/// carries everything the wording needs, so the notifier never touches the database.
/// </summary>
public abstract record NenNotification;

public sealed record BlockStartingSoon(BlockPresetType Type, string? Name, int MinutesLeft) : NenNotification;

public sealed record BlockStarted(BlockPresetType Type, string? Name) : NenNotification;

/// <summary>Only for a block that owns its own ending — see Block.MarkersPassedBetween.</summary>
public sealed record BlockEnded(BlockPresetType Type, string? Name) : NenNotification;

/// <summary>BlockName is a planned session's block name (Block.DisplayName); null when spontaneous.</summary>
public sealed record SessionStarted(bool IsPlanned, string? BlockName) : NenNotification;

/// <summary>BlockName is a planned session's block name (Block.DisplayName); null when spontaneous.</summary>
public sealed record SessionCompleted(bool IsPlanned, string? BlockName) : NenNotification;

public sealed record BreakStarted(BreakKind Kind, bool KeepsBlocking) : NenNotification;

public sealed record BreakEnded : NenNotification;

public sealed record ContractActivated(string Name) : NenNotification;

public sealed record ContractCompleted(string Name) : NenNotification;
