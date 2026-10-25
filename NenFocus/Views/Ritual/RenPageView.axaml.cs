using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NenFocus.ViewModels.Ritual;

namespace NenFocus.Views.Ritual;

public partial class RenPageView : UserControl
{
    public RenPageView()
    {
        InitializeComponent();

        var plannedBtn = this.FindControl<Button>("BrowseForAppButton");
        if (plannedBtn is not null)
            plannedBtn.Click += BrowseForApp_Click;

        var spontaneousBtn = this.FindControl<Button>("BrowseForAppButtonSpontaneous");
        if (spontaneousBtn is not null)
            spontaneousBtn.Click += BrowseForApp_Click;
    }

    private async void BrowseForApp_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var initialFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(@"C:\Program Files");

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an application",
            AllowMultiple = false,
            SuggestedStartLocation = initialFolder,
            FileTypeFilter = [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }]
        });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        if (DataContext is RenPageViewModel vm)
            vm.AddAppFromPath(path);
    }
}
