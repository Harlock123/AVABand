using Angband.Core.Definitions;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// AVABand's seasonal events (Seasons) in play. A character born while one runs keeps it for life
// (Player.Season, in the save and so in the replay): its monsters join the dungeon's at their depths,
// they may let fall its keepsakes, and the town says so when you arrive. A character with none — every
// one born before there were events, or out of season, or with Settings → "Seasonal events" off — and
// the daily dungeon, the weekly challenge and the tutorial play as they always did, rolling the dice
// exactly as before.
public sealed partial class GameSession
{
    /// <summary>The seasonal event this character was born in, or null (never in the daily dungeon, the weekly challenge or the tutorial).</summary>
    public SeasonDef? Season => Player.Season is null || Player.IsSeededRun || IsTutorial ? null : Seasons.Find(Player.Season);

    private (string Id, IReadOnlyList<MonsterRaceDef> Races)? _seasonalRaces;

    /// <summary>The monsters of this character's event, for the dungeon to draw on (none without one).</summary>
    private IReadOnlyList<MonsterRaceDef> SeasonalRaces()
    {
        if (Season is not { } season) return [];
        if (_seasonalRaces is { } cached && cached.Id == season.Id) return cached.Races;
        IReadOnlyList<MonsterRaceDef> races = [.. season.Monsters.Select(Data.Monster).OfType<MonsterRaceDef>()];
        _seasonalRaces = (season.Id, races);
        return races;
    }

    /// <summary>Arriving in the town: what the event looks like there, by day or by night.</summary>
    private void SeasonalTown()
    {
        if (Season is { } season) Publish(new MessageEvent(IsDaytime ? season.TownByDay : season.TownByNight));
    }

    /// <summary>
    /// One of the event's monsters slain: now and then (always, for its unique) it lets fall one of the
    /// event's keepsakes — an ordinary object under a themed name. No dice are rolled for any other monster.
    /// </summary>
    private void SeasonalKilled(Monster monster)
    {
        var race = monster.OriginalRace ?? monster.Race;
        if (Season is not { } season || !season.Monsters.Contains(race.Id) || season.Cosmetics.Count == 0) return;
        if (!race.IsUnique && !Rng.OneIn(Seasons.CosmeticChance)) return;
        var cosmetic = season.Cosmetics[Rng.RandInt0(season.Cosmetics.Count)];
        if (MakeCosmetic(cosmetic, Level.Depth) is { } item) DropNear(item, monster.Position);
    }

    /// <summary>A keepsake: a plain object of its kind, under its themed name.</summary>
    public Items.Item? MakeCosmetic(SeasonalCosmetic cosmetic, int depth)
    {
        if (Data.Object(cosmetic.Kind) is not { } kind) return null;
        var item = Objects.Create(kind);
        item.Cosmetic = cosmetic.Id;
        item.OriginDepth = depth;
        return item;
    }
}
