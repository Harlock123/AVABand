using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Picking up where you left off after a crash.</summary>
public sealed class CrashResumeUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-crash-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private MainWindowViewModel Start(bool resume)
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        if (!resume) vm.StartGame(42, "warrior");
        vm.UseSaves(new SaveStore(_dir), resume);
        return vm;
    }

    [AvaloniaFact]
    public void AfterACrash_TheNextStart_OffersToResume_FromTheLastSave()
    {
        var first = Start(resume: false);
        Assert.True(first.TrySave());
        // ...and the game dies without closing its window.

        MainWindow.ShowCreationOnFirstRun = false;
        var vm = Start(resume: true);
        Assert.True(vm.ShouldOfferResume);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show(); // asks as it opens
        Assert.True(vm.IsConfirming);
        Assert.StartsWith("AVABand didn't close properly last time. Resume ", vm.LastMessage);
        Assert.Contains("from the save made a moment ago?", vm.LastMessage);

        vm.Confirm(true);
        Assert.False(vm.IsConfirming);
        Assert.Contains("Resumed from the last save", vm.LastMessage);

        // Closing the window properly: the next start doesn't ask.
        window.Close();
        Assert.False(Start(resume: true).ShouldOfferResume);
    }

    [AvaloniaFact]
    public void Declining_OpensTheListOfSavedCharacters()
    {
        Start(resume: false).TrySave(); // then a crash
        var vm = Start(resume: true);
        var listed = false;
        vm.LoadRequested += () => listed = true;
        vm.OfferResume();
        vm.Confirm(false);
        Assert.True(listed);
    }

    [Theory]
    [InlineData(20, "a moment ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(180, "3 minutes ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(4 * 86400, "4 days ago")]
    public void Ago_SaysHowLongSince(int seconds, string said) =>
        Assert.Equal(said, MainWindowViewModel.Ago(TimeSpan.FromSeconds(seconds)));
}
