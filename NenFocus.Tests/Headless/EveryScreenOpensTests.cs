using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Main;
using NenFocus.ViewModels.Planner;
using NenFocus.ViewModels.Planner.ContractWizard;
using NenFocus.ViewModels.Presets;
using NenFocus.ViewModels.Ritual;
using NenFocus.Views.Shell;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 89 Slice B moved every view into a folder per app area; the ViewLocator finds a view by
/// mirroring its ViewModel's namespace and shows "Not Found: …" when it can't. This clicks
/// through the real window — every page, both Presets tabs, a whole Ritual to its Post-Session
/// screen, the contract wizard's four steps and the debug clock — and reads what is on screen.
/// (ViewLocationRuleTests checks the naming; this checks the screens a user actually reaches.)
/// </summary>
public class EveryScreenOpensTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
    private readonly RealApp _app = new(Now);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [AvaloniaFact]
    public async Task EveryScreenOpens_NoneShowsNotFound()
    {
        await _app.SeedPresetAsync("Deep Work", BlockPresetType.Focus);
        var shell = await _app.CreateShellAsync();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        await RealApp.SettleAsync();

        var seen = new List<string>();
        void Check(string screen)
        {
            seen.Add(screen);
            VisibleTexts(window).Should().NotContain(t => t.StartsWith("Not Found"), $"the {screen} screen should open");
        }

        foreach (var item in shell.NavItems)
        {
            item.SelectCommand.Execute(null);
            await RealApp.SettleAsync();
            Check(item.Label);
        }

        var presets = (PresetsPageViewModel)shell.NavItems.Single(i => i.Label == "Presets").PageViewModel;
        shell.NavItems.Single(i => i.Label == "Presets").SelectCommand.Execute(null);
        presets.ShowContractPresetsTabCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("Contract Presets");

        // A whole Ritual, Ten to Post-Session.
        var main = (MainPageViewModel)shell.NavItems[0].PageViewModel;
        shell.NavItems[0].SelectCommand.Execute(null);
        main.StartRitualCommand.Execute(null);
        await RealApp.SettleAsync();
        shell.IsRitualActive.Should().BeTrue();
        var ritual = shell.RitualViewModel!;
        Check("Ten");

        ((TenPageViewModel)ritual.CurrentStep).ContinueCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("Zetsu");
        var zetsu = (ZetsuPageViewModel)ritual.CurrentStep;
        zetsu.Goal = "Write the chapter";
        zetsu.ContinueCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("Ren");
        var ren = (RenPageViewModel)ritual.CurrentStep;
        ren.SelectPresetCommand.Execute(ren.Presets.First(c => c.Preset.Name == "Deep Work"));
        ren.ContinueCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("Hatsu");
        await ((HatsuPageViewModel)ritual.CurrentStep).StartSessionCommand.ExecuteAsync(null);
        await RealApp.SettleAsync();
        shell.IsRitualActive.Should().BeFalse("the session started");
        Check("Main with a running session");

        _app.Clock.SetNow(Now.AddMinutes(10));
        await main.EndSessionCommand.ExecuteAsync(null);
        await RealApp.SettleAsync();
        shell.IsPostSessionActive.Should().BeTrue();
        Check("Post-Session");
        await shell.PostSessionViewModel!.DoneCommand.ExecuteAsync(null);
        await RealApp.SettleAsync();

        // The contract wizard, all four steps.
        var planner = (PlannerPageViewModel)shell.NavItems.Single(i => i.Label == "Planner").PageViewModel;
        shell.NavItems.Single(i => i.Label == "Planner").SelectCommand.Execute(null);
        planner.OpenContractWizardCommand.Execute(null);
        await RealApp.SettleAsync();
        shell.IsContractWizardActive.Should().BeTrue();
        var wizard = shell.ContractWizardViewModel!;
        Check("wizard: basics");
        var basics = (ContractBasicsStepViewModel)wizard.CurrentStep;
        basics.Name = "Focused Week";
        basics.Goal = "Ship it";
        await RealApp.SettleAsync();
        basics.ContinueCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("wizard: mode");
        ((ContractModeStepViewModel)wizard.CurrentStep).ChooseSimpleCommand.Execute(null);
        await RealApp.SettleAsync();
        Check("wizard: rules");
        ((ContractRulesStepViewModel)wizard.CurrentStep).ContinueCommand.Execute(null);
        await RealApp.SettleAsync();
        wizard.CurrentStep.Should().BeOfType<ContractReviewStepViewModel>();
        Check("wizard: review");
        wizard.GoBackCommand.Execute(null);
        wizard.GoBackCommand.Execute(null);
        wizard.GoBackCommand.Execute(null);
        wizard.GoBackCommand.Execute(null);
        await RealApp.SettleAsync();
        shell.IsContractWizardActive.Should().BeFalse();

        shell.DebugClockViewModel.ToggleVisibleCommand.Execute(null);
        await RealApp.SettleAsync();
        shell.DebugClockViewModel.IsVisible.Should().BeTrue();
        Check("debug clock");

        seen.Should().HaveCount(17);
    }
}
