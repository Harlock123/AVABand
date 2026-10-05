namespace Angband.Core.Game;

/// <summary>A character's tallies for the statistics page (AVABand's own; kept in the save).</summary>
public sealed class PlayStats
{
    public long DamageDealt { get; set; }
    public long DamageTaken { get; set; }
    public long BiggestHit { get; set; }
    public long WorstHitTaken { get; set; }
    public int CloseCalls { get; set; }
    public long GoldFound { get; set; }
    public long GoldFromSelling { get; set; }
    public long GoldSpentInShops { get; set; }
    public int PotionsQuaffed { get; set; }
    public int ScrollsRead { get; set; }
    public int FoodEaten { get; set; }
    public int DevicesUsed { get; set; }
    public int SpellsCast { get; set; }
    public int MissilesFired { get; set; }
    public int TrapsDisarmed { get; set; }
    public int StairsTaken { get; set; }
    public int Uniques { get; set; }
}

// AVABand's own: the statistics page's tallies, counted from what the game already announces — damage
// dealt and taken (and the biggest of each), close calls (falling below a tenth of your hit points, and
// living), gold found, sold and spent, potions, scrolls, food, devices, spells, missiles, traps disarmed,
// stairs taken, uniques slain. Kills by monster come from the character's kill counts.
public sealed partial class GameSession
{
    public PlayStats Stats { get; private set; } = new();

    /// <summary>Loading a save: its tallies (none in older saves).</summary>
    internal void RestoreStats(PlayStats? stats) => Stats = stats ?? new PlayStats();

    private void TrackStats()
    {
        Events.Subscribe<MonsterDamagedEvent>(e =>
        {
            Stats.DamageDealt += e.Damage;
            Stats.BiggestHit = Math.Max(Stats.BiggestHit, e.Damage);
        });
        Events.Subscribe<PlayerHurtEvent>(e =>
        {
            Stats.DamageTaken += e.Damage;
            Stats.WorstHitTaken = Math.Max(Stats.WorstHitTaken, e.Damage);
            if (e.Hp > 0 && e.Hp * 10 < e.MaxHp && (e.Hp + e.Damage) * 10 >= e.MaxHp) Stats.CloseCalls++;
        });
        Events.Subscribe<ItemPickedUpEvent>(e => { if (e.Gold) Stats.GoldFound += e.Amount; });
        Events.Subscribe<ItemSoldEvent>(e => Stats.GoldFromSelling += e.Price);
        Events.Subscribe<ItemBoughtEvent>(e => Stats.GoldSpentInShops += e.Price);
        Events.Subscribe<ItemUsedEvent>(e =>
        {
            switch (e.Verb)
            {
                case "quaff": Stats.PotionsQuaffed++; break;
                case "read": Stats.ScrollsRead++; break;
                case "eat": Stats.FoodEaten++; break;
                case "activate": Stats.DevicesUsed++; break;
                default:
                    if (Data.Object(e.KindId)?.Base is "wand" or "staff" or "rod") Stats.DevicesUsed++;
                    break;
            }
        });
        Events.Subscribe<SpellCastEvent>(_ => Stats.SpellsCast++);
        Events.Subscribe<MissileFiredEvent>(_ => Stats.MissilesFired++);
        Events.Subscribe<TrapDisarmedEvent>(_ => Stats.TrapsDisarmed++);
        Events.Subscribe<StairsTakenEvent>(_ => Stats.StairsTaken++);
        Events.Subscribe<MonsterKilledEvent>(e => { if (e.IsUnique) Stats.Uniques++; });
    }

    /// <summary>The monsters killed most, most first (name, count).</summary>
    public IReadOnlyList<(string Name, int Count)> TopKills(int count = 10) =>
        [.. CharacterKills.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(count)
            .Select(kv => (Data.Monster(kv.Key)?.Name ?? kv.Key, kv.Value))];
}
