using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(NenFocus.Tests.Headless.TestAppBuilder))]

namespace NenFocus.Tests.Headless;

/// <summary>
/// Starts the real App (its styles and resources) on Avalonia's headless platform — no window on
/// screen — so [AvaloniaFact] tests can render real views, click them and read them. App's desktop
/// setup (shutdown handler, MainWindow) is skipped: headless has no classic desktop lifetime.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
