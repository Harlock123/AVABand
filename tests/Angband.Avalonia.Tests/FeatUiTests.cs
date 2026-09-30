using Angband.Avalonia.ViewModels;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>A feat done is said in the messages, saved beside the scores, and listed in Knowledge → Feats.</summary>
public sealed class FeatUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-feats-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [AvaloniaFact]
    public void AFeat_IsSaid_Saved_AndListed()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var records = new RecordStore(_dir);
        vm.UseRecords(records);
        Assert.StartsWith("0 of ", vm.CreateFeatsPage().Summary);

        vm.Game.Player.MaxDepth = 10;
        vm.Execute(new HoldCommand());
        Assert.Contains("Feat: Below the Town! (Reach 500 ft.)", vm.LastMessage + string.Join(" ", vm.EarlierMessages.Select(m => m.Text)));
        Assert.True(File.Exists(records.FeatsPath));
        Assert.Contains("depth_10", records.LoadFeats().Earned.Keys);

        var page = vm.CreateFeatsPage();
        Assert.StartsWith("1 of ", page.Summary);
        var row = page.Rows.Single(r => r.Name == "Below the Town");
        Assert.Equal("*", row.Glyph);
        Assert.Contains("First done by", row.Describe());
        Assert.NotNull(vm.CreateKnowledge().Feats);
    }
}
