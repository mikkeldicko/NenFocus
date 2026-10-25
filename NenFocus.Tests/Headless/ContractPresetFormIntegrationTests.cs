using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Converters;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 92, V9: the Contract Presets form turns its text boxes into a WorkHours and a whole-hour
/// floor at its edge, and the rest is ContractPreset.CheckSettings' answer, worded once — here
/// through the real composition root and a real SQLite file, onto the rendered form.
/// </summary>
public class ContractPresetFormIntegrationTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ContractPresetPageViewModel _vm;
    private readonly ContractPresetPageView _view;

    public ContractPresetFormIntegrationTests()
    {
        var clock = TestClock.Create();
        clock.SetTimeZoneOffset(TimeSpan.Zero);
        clock.SetNow(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Path.Combine(Path.GetTempPath(), $"NenFocusPresetFormTest_{Guid.NewGuid()}.db"));
        services.AddSingleton(clock);
        services.AddSingleton(Substitute.For<IEnforcementService>());
        services.AddSingleton(Substitute.For<INotificationService>());
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<NenFocusDbContext>().Database.Migrate();

        _vm = new ContractPresetPageViewModel(
            _provider.GetRequiredService<IQueryDispatcher>(), _provider.GetRequiredService<ICommandDispatcher>());
        _view = new ContractPresetPageView { DataContext = _vm };
        Show(_view);
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private async Task SaveNewAsync(string name, string? floorHours, string? workStart = null, string? workEnd = null)
    {
        _vm.NewPresetCommand.Execute(null);
        _vm.FormName = name;
        _vm.IsDailyFloorEnabled = floorHours is not null;
        if (floorHours is not null) _vm.DailyFloorHoursText = floorHours;
        _vm.IsWorkHoursEnabled = workStart is not null;
        if (workStart is not null) { _vm.WorkHoursStart = Time(workStart); _vm.WorkHoursEnd = Time(workEnd!); }
        Settle();

        await _vm.SaveCommand.ExecuteAsync(null);
        for (var i = 0; i < 5; i++) { Settle(); await Task.Delay(20); }
    }

    [AvaloniaFact]
    public async Task AFloorLongerThanTheWorkHours_IsRefusedWithTheDomainsReason()
    {
        await SaveNewAsync("Mornings", floorHours: "4", "09:00", "12:00");

        VisibleTexts(_view).Should().Contain("Daily floor cannot exceed the total work hours window.");
        _vm.IsFormVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AFloorThatFits_Saves()
    {
        await SaveNewAsync("Mornings", floorHours: "3", "09:00", "12:00");

        _vm.FormError.Should().BeNull();
        _vm.IsFormVisible.Should().BeFalse();
        VisibleTexts(_view).Should().Contain("Mornings");
    }

    [AvaloniaFact]
    public async Task NoWorkHoursAndNoFixedFloor_Saves()
    {
        await SaveNewAsync("Free Form", floorHours: null);

        _vm.FormError.Should().BeNull();
        VisibleTexts(_view).Should().Contain("Free Form");
    }

    [AvaloniaFact]
    public async Task AFractionalFloor_IsRefusedByTheForm()
    {
        await SaveNewAsync("Half Hours", floorHours: "1.5");

        VisibleTexts(_view).Should().Contain("Daily floor must be a whole number of hours.");
    }

    private static TimeSpan Time(string text) =>
        TimeOfDayToStringConverter.TryParse(text, out var time) ? time : throw new ArgumentException(text);

    [AvaloniaFact]
    public async Task WorkHoursEndingAtMidnight_ReopenAs2400_AndSaveUnchanged()
    {
        // TimeOnly keeps midnight as MaxValue, which used to reopen as 23:59 and save back as 23:59.
        await SaveNewAsync("Late", floorHours: null, "18:00", "24:00");
        _vm.Presets.Single(p => p.Name == "Late").WorkHoursEnd.Should().Be(TimeOnly.MaxValue);

        _vm.EditPresetCommand.Execute(_vm.Presets.Single(p => p.Name == "Late"));
        _vm.WorkHoursEnd.Should().Be(TimeSpan.FromDays(1));
        await _vm.SaveCommand.ExecuteAsync(null);
        for (var i = 0; i < 5; i++) { Settle(); await Task.Delay(20); }

        _vm.FormError.Should().BeNull();
        _vm.Presets.Single(p => p.Name == "Late").WorkHoursEnd.Should().Be(TimeOnly.MaxValue);
    }

    [AvaloniaFact]
    public async Task WorkHoursEndingBeforeTheyStart_AreRefusedByTheForm()
    {
        await SaveNewAsync("Backwards", floorHours: null, "12:00", "09:00");

        VisibleTexts(_view).Should().Contain("Work hours end must be after start.");
    }
}
