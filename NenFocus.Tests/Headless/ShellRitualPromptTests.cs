using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 92, A3, D′: the Shell's Ritual follows IRitualPrompt — open while the prompt names a block,
/// closed once it stops naming it, whatever the reason — and missed screens come only from the
/// heartbeat's sweep (IRecapRelay). Also the restart path: the Shell and Main page pick up what the
/// prompt and the active session already said before they were built.
/// </summary>
public class ShellRitualPromptTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 10, 0, DateTimeKind.Utc);

    private readonly IActiveSessionState _sessionState = Substitute.For<IActiveSessionState>();
    private readonly IRitualPrompt _prompt = Substitute.For<IRitualPrompt>();
    private readonly IRecapRelay _recaps = Substitute.For<IRecapRelay>();
    private readonly IActiveSessionMonitor _monitor = Substitute.For<IActiveSessionMonitor>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly FakeQueryDispatcher _queries = new();

    public ShellRitualPromptTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _monitor.Current.Returns(TimelineReading.None);
        _queries.Answer<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?>(q => new FocusBlockRitualContext
        {
            Preset = new BlockPresetDto { Id = Guid.NewGuid(), Name = "Deep Work", PresetType = BlockPresetType.Focus },
            Schedule = new FocusBlockSchedule { Slot = At.Slot(Now.AddMinutes(-10), Now.AddMinutes(50)) },
            LateBy = TimeSpan.Zero,
            IsLate = false,
            AlreadyHasSession = false,
        });
    }

    private ShellViewModel CreateShell()
    {
        var shell = new ShellViewModel(
            _queries, Substitute.For<ICommandDispatcher>(), _sessionState, _prompt, _recaps,
            _monitor, Substitute.For<IDisplayClock>(), _clock,
            Substitute.For<IDebugClockController>());
        Settle();
        return shell;
    }

    private void PromptNames(Guid? blockId)
    {
        _prompt.PendingFocusBlockId.Returns(blockId);
        _prompt.Changed += Raise.Event<EventHandler<Guid?>>(_prompt, blockId);
        Settle();
    }

    private void SweepAnnouncesAMiss()
    {
        var missed = new PostSessionDto { SessionId = Guid.NewGuid(), Status = SessionStatus.Missed };
        _recaps.PostSessionsReady += Raise.Event<EventHandler<IReadOnlyList<PostSessionDto>>>(_recaps, (IReadOnlyList<PostSessionDto>)[missed]);
        Settle();
    }

    [AvaloniaFact]
    public void AfterARestart_ABlockAlreadyAwaitingItsRitual_OpensIt()
    {
        _prompt.PendingFocusBlockId.Returns(Guid.NewGuid());

        var shell = CreateShell();

        shell.IsRitualActive.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AfterARestart_ASessionAlreadyRunning_ShowsOnTheMainPageAndNoRitual()
    {
        _sessionState.ActiveSession.Returns(new SessionDto { Id = Guid.NewGuid(), Goal = "Thesis", Status = SessionStatus.Active });

        var shell = CreateShell();

        shell.IsRitualActive.Should().BeFalse();
        var main = (NenFocus.ViewModels.Pages.MainPageViewModel)shell.CurrentPage;
        main.HasActiveSession.Should().BeTrue();
        main.SessionGoal.Should().Be("Thesis");
    }

    [AvaloniaFact]
    public void ThePromptNamingABlock_OpensItsRitual()
    {
        var shell = CreateShell();

        PromptNames(Guid.NewGuid());

        shell.IsRitualActive.Should().BeTrue();
    }

    // A planned session's Start: the block stops awaiting its Ritual, so the prompt stops naming it.
    [AvaloniaFact]
    public void ThePromptNoLongerNamingTheBlock_ClosesItsRitual()
    {
        var shell = CreateShell();
        PromptNames(Guid.NewGuid());

        PromptNames(null);

        shell.IsRitualActive.Should().BeFalse();
        shell.IsPostSessionActive.Should().BeFalse("only the sweep announces a miss");
    }

    // A Ritual left open past its block's end: in the same tick the sweep announces the miss,
    // then the prompt stops naming the block — the Ritual closes and the missed screen follows.
    [AvaloniaFact]
    public void ARitualLeftOpenPastItsEnd_ClosesAndTheMissedScreenFollows()
    {
        var shell = CreateShell();
        var block = Guid.NewGuid();
        PromptNames(block);

        SweepAnnouncesAMiss();
        shell.IsRitualActive.Should().BeTrue("the missed screen waits behind the open Ritual");
        PromptNames(null);

        shell.IsRitualActive.Should().BeFalse();
        shell.IsPostSessionActive.Should().BeTrue();
    }

    // Back-to-back blocks: the previous block's missed screen first, then the next block's Ritual.
    [AvaloniaFact]
    public async Task BackToBackBlocks_ShowThePreviousMissFirstThenTheNextRitual()
    {
        var shell = CreateShell();
        var previous = Guid.NewGuid();
        PromptNames(previous);

        SweepAnnouncesAMiss();
        PromptNames(Guid.NewGuid());

        shell.IsPostSessionActive.Should().BeTrue();
        shell.IsRitualActive.Should().BeFalse();

        await shell.PostSessionViewModel!.DoneCommand.ExecuteAsync(null);
        Settle();

        shell.IsRitualActive.Should().BeTrue();
    }

    // A Ritual queued behind another screen whose block stopped awaiting meanwhile is not opened.
    [AvaloniaFact]
    public async Task AQueuedRitualWhoseBlockNoLongerAwaits_IsNotOpened()
    {
        var shell = CreateShell();
        SweepAnnouncesAMiss(); // a screen is showing
        var block = Guid.NewGuid();
        PromptNames(block);                   // queued behind it
        _prompt.PendingFocusBlockId.Returns((Guid?)null); // its window closed meanwhile

        await shell.PostSessionViewModel!.DoneCommand.ExecuteAsync(null);
        Settle();

        shell.IsRitualActive.Should().BeFalse();
    }
}
