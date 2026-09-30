namespace Angband.Core.Game;

// Encumbrance. Angband 4.2: Strength sets a weight limit; half of it is carried unhindered, and every
// tenth of it beyond costs a point of speed. AVABand (the birth option birth_ava_burden, on by default):
// Strength's limit goes on growing where Angband's stalls (Magic.StatTables.AvaCarryLimit),
// Constitution adds a little (2 lb unhindered for each step above 15), worn gear weighs three
// quarters of what it would in the pack, and the burden has a name (Burdened, Strained,
// Overloaded). Either way, what raises the limit by a share adds up: the best bag of holding carried,
// (Porter) boots or cloaks worn (ava_egos.json), and — with AVABand's rules — Heroism or Berserk
// Strength (10%) and the race (ava_races.json, with the racial abilities: Half-Trolls +20%, Dwarves
// +10%, Hobbits and Kobolds -10%).
public sealed partial class GameSession
{
    private bool AvaBurden => Options[OptionIds.AvaBurden];

    /// <summary>The weight limit, in tenths of a pound (half is carried unhindered).</summary>
    private int CarryLimit(Func<int[], string, int> adj)
    {
        var limit = adj(AvaBurden ? Magic.StatTables.AvaCarryLimit : Magic.StatTables.CarryLimit, "str") * 10;
        if (AvaBurden)
            limit += 40 * Math.Max(0, Magic.StatTables.Index(Player.Stats.GetValueOrDefault("con", 15)) - Magic.StatTables.Index(15));
        return limit * (100 + CarryPercent) / 100;
    }

    /// <summary>What raises (or lowers) the weight limit by a share, in percent, all told.</summary>
    public int CarryPercent =>
        (Player.Inventory.BestBag?.Kind.CarryPercent ?? 0)
        + Player.Inventory.Equipped.Sum(i => i.Ego?.CarryPercent ?? 0)
        + (AvaBurden && (Player.Timed.Has(Effects.TimedIds.Hero) || Player.Timed.Has("berserk")) ? 10 : 0)
        + (Options[OptionIds.AvaRaces] ? Player.Race?.AvaCarryPercent ?? 0 : 0);

    /// <summary>The weight that counts against the limit: worn gear three quarters of it with AVABand's rules.</summary>
    public int BurdenWeight
    {
        get
        {
            var inv = Player.Inventory;
            if (!AvaBurden) return inv.TotalWeight;
            var worn = inv.Equipped.Sum(i => i.TotalWeight);
            return inv.TotalWeight - worn + worn * 3 / 4;
        }
    }

    /// <summary>The speed lost to weight: a point for each tenth of the limit beyond half of it.</summary>
    public int BurdenPenalty
    {
        get
        {
            var weight = BurdenWeight;
            var limit = Player.WeightLimit;
            return weight > limit / 2 ? (weight - limit / 2) / Math.Max(1, limit / 10) : 0;
        }
    }

    /// <summary>The burden by name, by what it costs: Unhindered (nothing yet); Burdened (-1); Strained (-2, -3); Overloaded (-4 or worse).</summary>
    public string BurdenName => BurdenPenalty switch { 0 => "Unhindered", 1 => "Burdened", <= 3 => "Strained", _ => "Overloaded" };
}
