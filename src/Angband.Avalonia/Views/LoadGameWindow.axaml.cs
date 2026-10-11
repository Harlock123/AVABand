using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Angband.Avalonia.Views;

public partial class LoadGameWindow : Window
{
    public LoadGameWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && DataContext is LoadGameViewModel vm && vm.LoadCommand.CanExecute(null))
                vm.LoadCommand.Execute(null);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not LoadGameViewModel vm) return;
        vm.Loaded += Close;
        vm.PickImport = PickImportAsync;
        vm.PickExport = PickExportAsync;
    }

    private static readonly FilePickerFileType SaveFiles = new("AVABand saves") { Patterns = ["*" + Angband.Core.Persistence.SaveGame.Extension] };

    private async Task<string?> PickImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a save (from another computer)",
            AllowMultiple = false,
            FileTypeFilter = [SaveFiles, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickExportAsync(string suggested)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export a save (to take to another computer)",
            SuggestedFileName = suggested,
            DefaultExtension = Angband.Core.Persistence.SaveGame.Extension,
            FileTypeChoices = [SaveFiles],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Opens the saves folder in the file manager (made first, if no game has been saved yet).</summary>
    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LoadGameViewModel vm) return;
        try
        {
            System.IO.Directory.CreateDirectory(vm.SavesFolder);
            if (!await Launcher.LaunchDirectoryInfoAsync(new System.IO.DirectoryInfo(vm.SavesFolder)))
                vm.Status = $"Your saves are in {vm.SavesFolder}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.Status = $"Your saves are in {vm.SavesFolder}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LoadGameViewModel vm && vm.LoadCommand.CanExecute(null)) vm.LoadCommand.Execute(null);
    }
}
