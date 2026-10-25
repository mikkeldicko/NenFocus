using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAvailableBreakTimerPresets;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Ritual;
using NSubstitute;
using NenFocus.Views.Ritual;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>The Ritual's Zetsu, Ren and Hatsu steps, rendered for real and driven by keyboard and mouse.</summary>
public class RitualStepViewTests
{
    private static readonly BlockPresetDto DeepWork = new()
    {
        Id = Guid.NewGuid(),
        Name = "Deep Work",
        PresetType = BlockPresetType.Focus,
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        BlockedApps = ["discord"],
        Websites = ["youtube.com", "reddit.com"]
    };

    private static readonly BlockPresetDto AllowlistOnly = new()
    {
        Id = Guid.NewGuid(),
        Name = "Docs Only",
        PresetType = BlockPresetType.Focus,
        WebsiteFilterType = WebsiteFilterType.Allowlist,
        Websites = ["learn.microsoft.com"]
    };

    [AvaloniaFact]
    public void Zetsu_TypingAGoalEnablesContinue()
    {
        var continued = false;
        var vm = new ZetsuPageViewModel(() => continued = true, () => { });
        var view = new ZetsuPageView { DataContext = vm };
        var window = Show(view);

        var continueButton = ButtonWithText(view, "Continue");
        continueButton.IsEffectivelyEnabled.Should().BeFalse("no goal has been written yet");

        Type(window, view.GetLogicalDescendants().OfType<TextBox>().Single(), "Finish chapter 3");

        vm.Goal.Should().Be("Finish chapter 3");
        continueButton.IsEffectivelyEnabled.Should().BeTrue();
        Click(window, continueButton);
        continued.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Ren_Spontaneous_ListsFocusPresetsAndPickingOneShowsItsExtraBlocking()
    {
        var queries = new FakeQueryDispatcher()
            .Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ => [DeepWork, AllowlistOnly]);
        var vm = new RenPageViewModel(queries, SessionOrigin.Spontaneous, null, () => { }, () => { });
        var view = new RenPageView { DataContext = vm };
        var window = Show(view);

        VisibleTexts(view).Should().Contain(["Ren", "Deep Work", "Docs Only"]);

        Click(window, ButtonWithText(view, "Deep Work"));
        vm.SelectedPreset.Should().Be(DeepWork);
        VisibleTexts(view).Should().Contain("← Change preset").And.NotContain("Docs Only");

        Click(window, ButtonWithText(view, "Deep Work")); // expands the selected card
        VisibleTexts(view).Should().Contain(["youtube.com", "reddit.com", "discord", "BLOCK EXTRA SITES THIS SESSION"]);

        Click(window, ButtonWithText(view, "← Change preset"));
        vm.SelectedPreset.Should().BeNull();
        VisibleTexts(view).Should().Contain("Docs Only");
    }

    [AvaloniaFact]
    public void Ren_AllowlistPresetOffersNoExtraSites()
    {
        var queries = new FakeQueryDispatcher()
            .Answer<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>(_ => [AllowlistOnly]);
        var vm = new RenPageViewModel(queries, SessionOrigin.Spontaneous, null, () => { }, () => { });
        var view = new RenPageView { DataContext = vm };
        var window = Show(view);

        Click(window, ButtonWithText(view, "Docs Only"));
        Click(window, ButtonWithText(view, "Docs Only"));

        VisibleTexts(view).Should().Contain(["ALLOWED SITES", "learn.microsoft.com"])
            .And.NotContain("BLOCK EXTRA SITES THIS SESSION");
    }

    [AvaloniaFact]
    public void Ren_Planned_ShowsOnlyTheLockedPresetAndAddsAnExtraSite()
    {
        var vm = new RenPageViewModel(new FakeQueryDispatcher(), SessionOrigin.Planned, DeepWork, () => { }, () => { });
        var view = new RenPageView { DataContext = vm };
        var window = Show(view);

        VisibleTexts(view).Should().Contain("Deep Work").And.NotContain("← Change preset");

        Click(window, ButtonWithText(view, "Deep Work"));
        var siteBox = view.GetLogicalDescendants().OfType<TextBox>().Single(IsShown);
        Type(window, siteBox, "https://www.twitch.tv/somebody");
        Click(window, ButtonWithText(view, "Add"));

        vm.AdditionalBlockedSites.Should().Equal("twitch.tv");
        VisibleTexts(view).Should().Contain("twitch.tv");
    }

    [AvaloniaFact]
    public async Task Hatsu_Spontaneous_PickingDurationAndTimerThenStartSendsThem()
    {
        var classic = new BreakTimerPresetDto { Id = Guid.NewGuid(), Name = "Classic", WorkMinutes = 25, ShortBreakMinutes = 5, CyclesBeforeLongBreak = 4, LongBreakMinutes = 15 };
        var queries = new FakeQueryDispatcher()
            .Answer<GetAvailableBreakTimerPresetsQuery, IReadOnlyList<BreakTimerPresetDto>>(_ => [classic]);
        var commands = Substitute.For<ICommandDispatcher>();
        commands.DispatchAsync(Arg.Any<StartSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(StartSessionResult.Ok(Guid.NewGuid()));
        var started = false;
        var presetId = Guid.NewGuid();
        var vm = new HatsuPageViewModel(commands, queries, TestClock.Create(),
            () => new HatsuData { Goal = "Write", PresetId = presetId }, () => started = true, () => { });
        var view = new HatsuPageView { DataContext = vm };
        var window = Show(view);

        var oneHour = ButtonWithText(view, "1 h");
        Click(window, oneHour);
        oneHour.Classes.Should().Contain("active");
        ButtonWithText(view, "0 h").Classes.Should().NotContain("active");

        ButtonWithText(view, "No timer").Classes.Should().Contain("active");
        Click(window, ButtonWithText(view, "Classic"));
        ButtonWithText(view, "Classic").Classes.Should().Contain("active");
        ButtonWithText(view, "No timer").Classes.Should().NotContain("active");

        Click(window, ButtonWithText(view, "START SESSION"));

        await commands.Received(1).DispatchAsync(
            Arg.Is<StartSessionCommand>(c => c.DurationMinutes == 90 && c.BreakTimerPresetId == classic.Id && c.PresetId == presetId),
            Arg.Any<CancellationToken>());
        started.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Hatsu_ZeroDurationShowsTheErrorAndDisablesStart()
    {
        var vm = new HatsuPageViewModel(Substitute.For<ICommandDispatcher>(), new FakeQueryDispatcher(),
            TestClock.Create(), () => new HatsuData(), () => { }, () => { });
        var view = new HatsuPageView { DataContext = vm };
        var window = Show(view);

        Click(window, ButtonWithText(view, "0 min"));

        VisibleTexts(view).Should().Contain("Please select a duration greater than zero.");
        ButtonWithText(view, "START SESSION").IsEffectivelyEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Hatsu_OffersUpToTwelveHours_AsLongAsAFocusBlock()
    {
        // One rule for every focus period, FocusBlock.MaxLength (Gap 89 Slice B, A1) — it used to stop at 8 h.
        var vm = new HatsuPageViewModel(Substitute.For<ICommandDispatcher>(), new FakeQueryDispatcher(),
            TestClock.Create(), () => new HatsuData(), () => { }, () => { });
        var view = new HatsuPageView { DataContext = vm };
        var window = Show(view);

        Click(window, ButtonWithText(view, "12 h"));
        Click(window, ButtonWithText(view, "0 min"));
        ButtonWithText(view, "START SESSION").IsEffectivelyEnabled.Should().BeTrue("12 h is a Focus block's longest");

        Click(window, ButtonWithText(view, "15 min"));
        ButtonWithText(view, "START SESSION").IsEffectivelyEnabled.Should().BeFalse("12 h 15 m is longer than any Focus block");
    }
}
