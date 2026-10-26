using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Application.Services;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class NotifierTests
{
    private readonly INotificationService _toaster = Substitute.For<INotificationService>();

    private async Task ShowsAsync(NenNotification fact, string title, string body)
    {
        await new Notifier(_toaster).NotifyAsync(fact);
        await _toaster.Received(1).ShowAsync(title, body, Arg.Any<CancellationToken>());
    }

    [Fact]
    public Task BlockStartingSoon_Focus() => ShowsAsync(
        new BlockStartingSoon(BlockPresetType.Focus, "Thesis", 5),
        "Focus block starting soon", "\"Thesis\" starts in 5 minutes. Get ready for the Ritual.");

    [Fact]
    public Task BlockStartingSoon_BackgroundUntitledOneMinute() => ShowsAsync(
        new BlockStartingSoon(BlockPresetType.Background, null, 1),
        "Background block starting soon", "Your Background block starts in 1 minute. Blocking turns on then.");

    [Fact]
    public Task BlockStarted_Focus() => ShowsAsync(
        new BlockStarted(BlockPresetType.Focus, "Thesis"),
        "Focus block started", "\"Thesis\" has started. Blocking is on. Complete the Ritual to begin your session.");

    [Fact]
    public Task BlockStarted_Background() => ShowsAsync(
        new BlockStarted(BlockPresetType.Background, "Socials off"),
        "Background block started", "\"Socials off\" has started. Blocking is on.");

    [Fact]
    public Task BlockEnded_Background() => ShowsAsync(
        new BlockEnded(BlockPresetType.Background, "Socials off"),
        "Background block ended", "\"Socials off\" has ended. Blocking is off.");

    [Fact]
    public Task BlockEnded_FocusWithoutSession() => ShowsAsync(
        new BlockEnded(BlockPresetType.Focus, null),
        "Focus block ended", "Your Focus block ended without a session.");

    [Fact]
    public Task SessionStarted_Planned() => ShowsAsync(
        new SessionStarted(FocusBlockBookingType.Planned, "Thesis"), "Nen session started", "\"Thesis\" is on. Stay locked in.");

    [Fact]
    public Task SessionStarted_Spontaneous() => ShowsAsync(
        new SessionStarted(FocusBlockBookingType.Spontaneous, null), "Nen session started", "Focus session is now active. Stay locked in.");

    [Fact]
    public Task SessionCompleted_Planned() => ShowsAsync(
        new SessionCompleted(FocusBlockBookingType.Planned, "Thesis"), "Session complete", "\"Thesis\" complete. Great work.");

    [Fact]
    public Task SessionCompleted_PlannedUntitled() => ShowsAsync(
        new SessionCompleted(FocusBlockBookingType.Planned, null), "Session complete", "Your Focus block complete. Great work.");

    [Fact]
    public Task SessionCompleted_Spontaneous() => ShowsAsync(
        new SessionCompleted(FocusBlockBookingType.Spontaneous, null), "Session complete", "Great work. Your focus session has ended.");

    [Fact]
    public Task BreakStarted_Short() => ShowsAsync(
        new BreakStarted(BreakKind.ShortBreak, KeepsBlocking: false), "Break time", "Step away and recharge.");

    [Fact]
    public Task BreakStarted_LongKeepsBlocking() => ShowsAsync(
        new BreakStarted(BreakKind.LongBreak, KeepsBlocking: true),
        "Long break time", "Step away and recharge. Blocking stays on (contract rule).");

    [Fact]
    public Task BreakEnded() => ShowsAsync(new BreakEnded(), "Break over", "Back to focus.");

    [Fact]
    public Task ContractActivated() => ShowsAsync(
        new ContractActivated("Study Month"), "Contract started", "Your contract \"Study Month\" is now active.");

    [Fact]
    public Task ContractCompleted() => ShowsAsync(
        new ContractCompleted("Study Month"), "Contract complete", "Your contract \"Study Month\" has ended.");
}
