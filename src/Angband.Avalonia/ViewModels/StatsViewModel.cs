using System.Globalization;
using Angband.Core.Game;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line of the statistics page: what, and how many (a heading has no value).</summary>
public sealed record StatsLine(string What, string Value, bool IsHeading = false)
{
    public string Weight => IsHeading ? "SemiBold" : "Normal";
    public string Color => IsHeading ? "#8fc1ff" : "#dddddd";
}

/// <summary>The statistics page (AVABand's own): a character's tallies, and the monsters they've killed most.</summary>
public sealed class StatsViewModel
{
    public StatsViewModel(GameSession game)
    {
        var s = game.Stats;
        var p = game.Player;
        string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
        Title = "Statistics";
        Summary = $"{p.Name} the {p.Race?.Name} {p.Class?.Name}, level {p.Level}, deepest {p.MaxDepth * game.Data.Constants.FeetPerLevel} ft";
        Rows =
        [
            new("Fighting", "", true),
            new("Monsters killed", N(game.CharacterKills.Values.Sum())),
            new("Uniques slain", N(s.Uniques)),
            new("Damage dealt", N(s.DamageDealt)),
            new("Biggest hit", N(s.BiggestHit)),
            new("Damage taken", N(s.DamageTaken)),
            new("Worst hit taken", N(s.WorstHitTaken)),
            new("Close calls (below a tenth of your hit points, and lived)", N(s.CloseCalls)),
            new("Gold", "", true),
            new("Found in the dungeon", N(s.GoldFound)),
            new("From selling", N(s.GoldFromSelling)),
            new("Spent in shops", N(s.GoldSpentInShops)),
            new("What you've used", "", true),
            new("Potions quaffed", N(s.PotionsQuaffed)),
            new("Scrolls read", N(s.ScrollsRead)),
            new("Food eaten", N(s.FoodEaten)),
            new("Wands, staves, rods and activations used", N(s.DevicesUsed)),
            new("Spells cast", N(s.SpellsCast)),
            new("Missiles fired", N(s.MissilesFired)),
            new("Traps disarmed", N(s.TrapsDisarmed)),
            new("Stairs taken", N(s.StairsTaken)),
        ];
        var top = game.TopKills();
        Kills = top.Count == 0 ? [new("Nothing killed yet", "")] : [.. top.Select(k => new StatsLine(k.Name, N(k.Count)))];
    }

    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<StatsLine> Rows { get; }
    public IReadOnlyList<StatsLine> Kills { get; }
}

public sealed partial class MainWindowViewModel
{
    public event Action<StatsViewModel>? StatsRequested;

    public StatsViewModel CreateStats() => new(_game);

    /// <summary>Game → Statistics…</summary>
    [RelayCommand]
    public void ShowStats() => StatsRequested?.Invoke(CreateStats());
}
