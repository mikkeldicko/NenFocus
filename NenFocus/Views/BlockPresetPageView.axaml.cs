using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NenFocus.ViewModels.Pages;
using System.IO;

namespace NenFocus.Views;

public partial class BlockPresetPageView : UserControl
{
    public BlockPresetPageView() => InitializeComponent();

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
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } }
            }
        });

        if (files.Count > 0 && DataContext is BlockPresetPageViewModel vm)
        {
            var processName = Path.GetFileNameWithoutExtension(files[0].Path.LocalPath).ToLower();
            vm.AddAppFromExePath(processName);
        }
    }
}