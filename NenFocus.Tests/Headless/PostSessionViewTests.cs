using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>The Post-Session screen, rendered for real: its stats, blocked attempts and reflection.</summary>
public class PostSessionViewTests
{
    private static PostSessionDto Session(SessionStatus status) => new()
    {
        SessionId = Guid.NewGuid(),
        Status = status,
        Goal = "Finish chapter 3",
        PresetName = "Deep Work",
        StartedAt = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc),
        EndedAt = new DateTime(2026, 9, 29, 8, 25, 0, DateTimeKind.Utc),
        PlannedDurationMinutes = 30,
        ActualDurationMinutes = 25,
        BreaksTaken = 1,
        BlockedWebsiteAttempts = new Dictionary<string, int> { ["youtube.com"] = 3 },
        TotalBlockedAttempts = 3
    };

    [AvaloniaFact]
    public void Completed_ShowsTheSummaryAndBlockedAttempts()
    {
        var view = new PostSessionView { DataContext = new PostSessionViewModel(Session(SessionStatus.Completed), Substitute.For<ICommandDispatcher>(), () => { }) };
        Show(view);

        VisibleTexts(view).Should().Contain([
            "SESSION COMPLETE", "Finish chapter 3", "25m", "30m", "Deep Work",
            "BLOCKED ATTEMPTS", "youtube.com", "3×", "How did the session go? What did you accomplish?"]);
    }

    [AvaloniaFact]
    public void Missed_WearsTheMissedCopy()
    {
        var view = new PostSessionView { DataContext = new PostSessionViewModel(Session(SessionStatus.Missed), Substitute.For<ICommandDispatcher>(), () => { }) };
        Show(view);

        VisibleTexts(view).Should().Contain(["SESSION MISSED", "What got in the way?"]).And.NotContain("SESSION COMPLETE");
    }

    [AvaloniaFact]
    public async Task TypedReflectionIsSavedWhenDoneIsClicked()
    {
        var data = Session(SessionStatus.Completed);
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<SaveReflectionCommand>(), Arg.Any<CancellationToken>()).Returns(SaveReflectionResult.Ok());
        commands.DispatchAsync(Arg.Any<AcknowledgePostSessionCommand>(), Arg.Any<CancellationToken>()).Returns(AcknowledgePostSessionResult.Ok());
        var done = false;
        var view = new PostSessionView { DataContext = new PostSessionViewModel(data, commands, () => done = true) };
        var window = Show(view);

        Type(window, view.GetLogicalDescendants().OfType<TextBox>().Single(), "Went well");
        Click(window, ButtonWithText(view, "DONE"));

        await commands.Received(1).DispatchAsync(
            Arg.Is<SaveReflectionCommand>(c => c.SessionId == data.SessionId && c.Reflection == "Went well"), Arg.Any<CancellationToken>());
        await commands.Received(1).DispatchAsync(
            Arg.Is<AcknowledgePostSessionCommand>(c => c.SessionId == data.SessionId), Arg.Any<CancellationToken>());
        done.Should().BeTrue();
    }
}
