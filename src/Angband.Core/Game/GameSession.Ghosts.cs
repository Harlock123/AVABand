using System.Globalization;
using System.Text.Json.Nodes;
using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Randomness;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// A fallen character as its ghost needs it (AVABand's ghosts): copied from its headstone into the save,
/// so loading the game and playing back its replay never need the graveyard.
/// </summary>
public sealed class FallenGhost
{
    /// <summary>Which end it was (<see cref="FallenRecord.Key"/>).</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    /// <summary>The depth it fell at: where its ghost walks.</summary>
    public int Depth { get; set; }
    public string KilledBy { get; set; } = "";
    public DateTime DateUtc { get; set; }
    /// <summary>What it wore at the end (the save file's form of each item); null on an older headstone.</summary>
    public List<string>? Equipment { get; set; }
    /// <summary>It played with random artifacts: an artifact it wore was its own game's.</summary>
    public bool Randarts { get; set; }

    public static FallenGhost From(FallenRecord f) => new()
    {
        Key = f.Key, Name = f.Name, Race = f.Race, Class = f.Class, Level = f.Level, Depth = f.Depth, KilledBy = f.KilledBy,
        DateUtc = f.DateUtc, Equipment = f.Equipment is null ? null : [.. f.Equipment], Randarts = f.Randarts,
    };
}

/// <summary>A character's ghosts (saved with it): the fallen it may meet, those it has met, and those it laid to rest.</summary>
public sealed class GhostLog
{
    /// <summary>The fallen the graveyard told of when the game was last started or loaded (newest first), and any met since.</summary>
    public List<FallenGhost> Fallen { get; set; } = [];
    /// <summary>Ghosts this character has met (each walks once for it, until laid to rest).</summary>
    public List<string> Met { get; set; } = [];
    /// <summary>Ghosts this character laid to rest.</summary>
    public List<string> Laid { get; set; } = [];
    /// <summary>The ghosts that pleaded with this character to avenge them, and how each stands (none in an older save).</summary>
    public List<GhostPlea> Pleas { get; set; } = [];
}

/// <summary>
/// A pleading ghost (AVABand's): one that asks you to slay what killed it, and gives you what it carried
/// when you do. Saved with the character, so a plea left unanswered can be taken up again.
/// </summary>
public sealed class GhostPlea
{
    public const string Pleading = "pleading", Left = "left", Spurned = "spurned", Avenged = "avenged";

    /// <summary>Which ghost (its headstone's key).</summary>
    public string Key { get; set; } = "";
    /// <summary>The race of what killed it.</summary>
    public string Killer { get; set; } = "";
    /// <summary>
    /// How it stands: <see cref="Pleading"/> (it waits on the level), <see cref="Left"/> (you went, and it
    /// may walk and plead again), <see cref="Spurned"/> (you struck it, and it fights) or <see cref="Avenged"/>.
    /// </summary>
    public string Stage { get; set; } = Pleading;
    /// <summary>You have heard its plea (it pleads when first seen): the quest log has it from then.</summary>
    public bool Spoken { get; set; }
}

/// <summary>A fallen character's ghost laid to rest (the app marks its headstone; <paramref name="Key"/> is the headstone's).</summary>
public sealed record GhostLaidToRestEvent(string Key, string Name) : IGameEvent;

/// <summary>A ghost seen for the first time: what it says — its wail, or, for a pleading ghost, its plea.</summary>
public sealed record GhostSightedEvent(Monster Monster, FallenGhost Fallen, string Words, bool Pleading) : IGameEvent;

/// <summary>A pleading ghost avenged: its thanks, as it is laid to rest (<paramref name="Ghost"/> is gone from the level).</summary>
public sealed record GhostAvengedEvent(Monster Ghost, FallenGhost Fallen, string Words) : IGameEvent;

/// <summary>
/// AVABand's ghosts: the monster a fallen character's ghost is — its level, life, speed, blows and spells
/// from the character's class, level and the depth it fell at — and which headstones may send one.
/// </summary>
public static class FallenGhosts
{
    /// <summary>One time in this many, an eligible ghost walks on a new level at its depth.</summary>
    public const int Chance = 4;

    /// <summary>The most fallen a save carries (the newest; each with its gear).</summary>
    public const int MaxKept = 40;

    /// <summary>What a ghost's race id starts with (then its headstone's key).</summary>
    public const string RacePrefix = "ghost:";

    /// <summary>The existing tile a ghost is drawn with, in every tileset.</summary>
    public const string Tile = "ghost";

    /// <summary>
    /// The headstones whose ghosts may walk: not a winner's (they retired), not one who died in the town,
    /// and not one laid to rest — the newest <see cref="MaxKept"/>. (Cheats are never buried.)
    /// </summary>
    public static List<FallenGhost> FromGraveyard(Graveyard yard, int max = MaxKept) =>
        [.. yard.Fallen.Where(f => !f.Won && f.Depth > 0 && f.LaidToRestBy is null).Take(max).Select(FallenGhost.From)];

    /// <summary>
    /// A ghost's level: the depth it fell at and three more, and a quarter of however far the character
    /// was past that depth — a unique's strength there.
    /// </summary>
    public static int LevelOf(FallenGhost g) => Math.Clamp(g.Depth + 3 + Math.Max(0, g.Level - g.Depth) / 4, 1, 100);

    /// <summary>The class it was (by name, as the headstone has it; a multiclass too); null if the game no longer has it.</summary>
    public static ClassDef? ClassOf(GameData data, FallenGhost g) =>
        data.Classes.Concat(data.Multiclasses).FirstOrDefault(c => c.Name == g.Class);

    /// <summary>Each class's colour, for its ghost's letter.</summary>
    private static string Color(string classId) => classId switch
    {
        "mage" => "LightRed",
        "priest" => "LightGreen",
        "rogue" => "LightBlue",
        "ranger" => "Green",
        "paladin" => "Yellow",
        "druid" => "LightUmber",
        "necromancer" => "Violet",
        "blackguard" => "Orange",
        _ when classId.StartsWith(Multiclass.Fighter + "_", StringComparison.Ordinal) => "LightViolet",
        _ => "LightSlate", // a warrior (and a class no longer known)
    };

    /// <summary>
    /// The spells each class's ghost may have, from its realm's (the level the character must have
    /// reached for each, then the monster spell standing for it): what it attacks with, and what else.
    /// </summary>
    private static ((int Level, string Spell)[] Attacks, (int Level, string Spell)[] Others) SpellsOf(string classId) => classId switch
    {
        "mage" => ([(1, "MISSILE"), (10, "BO_COLD"), (20, "BO_FIRE"), (25, "BA_COLD"), (30, "BO_ACID"), (35, "BA_FIRE"), (42, "BO_MANA")],
                   [(3, "BLINK"), (12, "CONF"), (30, "TPORT")]),
        "rogue" => ([(5, "MISSILE"), (15, "BA_POIS")], [(5, "BLINK"), (20, "SLOW"), (30, "TPORT")]),
        "priest" => ([(1, "WOUND"), (15, "BA_HOLY")], [(1, "SCARE"), (5, "HEAL"), (15, "BLIND"), (30, "HOLD")]),
        "paladin" => ([(35, "BA_HOLY")], [(5, "SCARE"), (15, "HEAL")]),
        "necromancer" => ([(1, "BO_NETH"), (30, "BA_DARK"), (40, "BA_NETH")], [(1, "DARKNESS"), (5, "SCARE"), (10, "BLIND"), (20, "HOLD"), (25, "DRAIN_MANA")]),
        "blackguard" => ([], [(1, "SCARE"), (15, "HASTE"), (20, "TELE_SELF_TO")]),
        "druid" => ([(1, "BA_POIS"), (7, "BO_ELEC"), (15, "BO_ICE"), (25, "BA_ELEC"), (40, "STORM")], [(3, "CONF"), (20, "SLOW"), (30, "HEAL")]),
        "ranger" => ([], [(20, "HEAL")]),
        _ => ([], []),
    };

    /// <summary>
    /// The monster a fallen character's ghost is: a unique undead ('G', in its class's colour) that walks
    /// through walls, of <see cref="LevelOf"/>; with ten hit points a level and the level squared again
    /// (more for a sturdy class, less for a frail one), a little faster the deeper; a cold touch and its
    /// class's blows; and, for a caster, three of the attack spells it could cast and two of the rest.
    /// </summary>
    public static MonsterRaceDef BuildRace(GameData data, FallenGhost g)
    {
        var level = LevelOf(g);
        var cls = ClassOf(data, g);
        var classId = cls?.Id ?? "warrior";
        // A multiclass casts as its caster at two-thirds of its level, and fights as a warrior.
        var multi = classId.StartsWith(Multiclass.Fighter + "_", StringComparison.Ordinal);
        var caster = multi ? classId[(Multiclass.Fighter.Length + 1)..] : classId;
        var castingLevel = multi ? g.Level * 2 / 3 : g.Level;
        var fighter = classId is "warrior" or "blackguard" or "paladin" || multi;

        var hp = (10 * level + level * level) * (80 + 5 * (cls?.HitDie ?? 9)) / 100;
        var dice = new Dice(Math.Min(12, 1 + level / 8), 8);
        var drain = level < 20 ? "exp_10" : level < 40 ? "exp_20" : level < 60 ? "exp_40" : "exp_80";
        var blows = new List<MonsterBlowDef> { new() { Method = "touch", Effect = "cold", Damage = dice } };
        if (fighter) blows.AddRange(Enumerable.Repeat(new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = dice }, 3));
        else if (classId is "rogue" or "ranger") blows.AddRange(Enumerable.Repeat(new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = dice with { Sides = 6 } }, 2));
        else blows.Add(new() { Method = "wail", Effect = "terrify", Damage = Dice.Zero });
        if (classId is "necromancer" or "blackguard") blows.Add(new() { Method = "touch", Effect = drain, Damage = Dice.Zero });

        var (attacks, others) = SpellsOf(caster);
        var spells = attacks.Where(s => s.Level <= castingLevel).TakeLast(3)
            .Concat(others.Where(s => s.Level <= castingLevel).TakeLast(2)).Select(s => s.Spell).ToList();
        var spellFrequency = spells.Count == 0 ? 0 : multi || fighter || classId is "rogue" or "ranger" ? 6 : 3;
        if (classId == "ranger") spells.Add("ARROW");

        var flags = new HashSet<string>(StringComparer.Ordinal)
        {
            MonsterFlags.Unique, MonsterFlags.Undead, MonsterFlags.Evil, "COLD_BLOOD", "SPIRIT", MonsterFlags.PassWall, "IM_COLD", "IM_POIS",
            MonsterFlags.NoConf, MonsterFlags.NoSleep, MonsterFlags.NoStun, "NO_HOLD", MonsterFlags.MoveBody,
        };
        if (spells.Count > 0 && !fighter) flags.Add(MonsterFlags.Smart);
        var feet = g.Depth * data.Constants.FeetPerLevel;
        return new MonsterRaceDef
        {
            Id = RacePrefix + g.Key,
            Name = $"The Ghost of {g.Name}",
            Glyph = 'G',
            Base = "ghost",
            Color = Color(classId),
            Depth = level,
            Rarity = 1,
            Speed = Math.Min(20, 2 + level / 4),
            HitPoints = Math.Max(10, hp),
            Armour = 12 + level * 3 / 2,
            Sleep = 10,
            Hearing = 20,
            SpellFrequency = spellFrequency,
            InnateFrequency = classId == "ranger" ? 4 : 0,
            Spells = spells,
            Experience = level * 10 + g.Level * 5,
            Blows = blows,
            Flags = flags,
            TileAs = Tile,
            Description = $"The restless shade of {g.Name} the {g.Race} {g.Class}, who fell here at {feet} ft at level {g.Level}, "
                          + $"killed by {g.KilledBy}, on {g.DateUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}. "
                          + "It is still clad in what it wore in life, and will not rest until it is beaten.",
        };
    }

    /// <summary>What a ghost wails the first time it is seen (the same line every time for the same ghost).</summary>
    public static string Wail(FallenGhost g)
    {
        var killer = Killer(g);
        string[] lines =
        [
            $"{killer} ended me here. You will not leave so easily!",
            $"I was {g.Name} the {g.Class}! {killer} took everything from me, here, in the dark...",
            $"Go back! {killer} is still down here... and so am I.",
            "Give me back my rest, or join me in the dark!",
        ];
        var hash = 17L;
        foreach (var c in g.Key) hash = hash * 31 + c;
        return lines[(int)(Math.Abs(hash) % lines.Length)];
    }

    /// <summary>One ghost in this many pleads with you instead of fighting (when what killed it can be found).</summary>
    public const int PleaChance = 3;

    /// <summary>A number from a ghost's headstone key (and a game's seed): the same every time, and no dice rolled.</summary>
    private static long Hash(string key, ulong seed = 0)
    {
        var hash = 17L;
        foreach (var c in key) hash = hash * 31 + c;
        hash ^= (long)(seed * 0x9E3779B97F4A7C15UL);
        return (hash ^ (hash >>> 29)) & long.MaxValue;
    }

    /// <summary>Whether a ghost pleads with the character of this game rather than fighting (one in <see cref="PleaChance"/>, by its key and the seed).</summary>
    public static bool Pleads(FallenGhost g, ulong seed) => Hash(g.Key, seed) % PleaChance == 0;

    /// <summary>
    /// The monster that killed a fallen character, from its headstone's words ("a cave orc", "an Easterling
    /// warrior", "Grip, Farmer Maggot's Dog", or a plural): null for anything else — a trap, poison,
    /// starvation, another ghost — and for a quest's own foes (Sauron, Morgoth), who walk only where it puts them.
    /// </summary>
    public static MonsterRaceDef? KillerRace(GameData data, string killedBy)
    {
        var name = killedBy.Trim();
        if (name.Length == 0) return null;
        var names = new List<string> { name };
        foreach (var article in new[] { "a ", "an ", "the ", "some " })
            if (name.StartsWith(article, StringComparison.OrdinalIgnoreCase)) names.Add(name[article.Length..]);
        bool Named(MonsterRaceDef r) => names.Any(n => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(r.Plural, n, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(r.Name + "s", n, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(r.Name + "es", n, StringComparison.OrdinalIgnoreCase));
        return data.Monsters.FirstOrDefault(r => Named(r) && !r.Has(MonsterFlags.Questor));
    }

    /// <summary>What killed it, as its headstone has it, to start a sentence with.</summary>
    private static string Killer(FallenGhost g) =>
        g.KilledBy.Length == 0 ? "Something" : char.ToUpperInvariant(g.KilledBy[0]) + g.KilledBy[1..];

    /// <summary>What a pleading ghost says the first time it is seen (the same line every time for the same ghost).</summary>
    public static string Plea(FallenGhost g)
    {
        var killer = Killer(g);
        string[] lines =
        [
            $"I was {g.Name} the {g.Race} {g.Class}. {killer} cut me down here. Put it to rest, and what I carried is yours.",
            $"Hold! I mean you no harm. {killer} still walks these halls. Slay it, and take what was mine.",
            $"{killer} ended me, and I cannot rest while it lives. Avenge me, and my things are yours.",
            $"Please... I was {g.Name}. {killer} took my life here. End it, and I will give you all I wore.",
        ];
        return lines[(int)(Hash(g.Key) / 7 % lines.Length)];
    }

    /// <summary>What a pleading ghost says as it is avenged (the same line every time for the same ghost).</summary>
    public static string Thanks(FallenGhost g)
    {
        string[] lines =
        [
            "It is done. Take what was mine, and walk on in the light.",
            "At last... I can rest. These were mine; may they serve you better.",
            "You kept your word. Remember me when you go deeper.",
        ];
        return lines[(int)(Hash(g.Key) % lines.Length)];
    }

    /// <summary>What a ghost of a headstone without its gear drops instead, by class: weapons and armour, or rings, amulets and devices.</summary>
    public static IReadOnlyCollection<string> RewardBases(string classId) => classId switch
    {
        "mage" or "necromancer" => ["ring", "amulet", "wand", "rod", "staff", "cloak"],
        "priest" or "druid" => ["amulet", "ring", "hafted", "staff", "soft_armour", "cloak"],
        "rogue" => ["sword", "sling", "soft_armour", "ring", "cloak", "boots"],
        "ranger" => ["bow", "sword", "soft_armour", "cloak", "boots"],
        _ => ["sword", "hafted", "polearm", "hard_armour", "shield", "helm", "gloves", "boots"],
    };
}

// AVABand's ghosts of fallen characters (the birth option birth_ava_ghosts): the app hands each game the
// graveyard's fallen as it is started or loaded (and before its replay starts recording, so the replay
// has them too), and the save keeps them. A new level at the depth one of them fell at may — one time in
// four for each — bring its ghost, somewhere out of sight of where you arrive, one a level; it walks once
// for each character until it is laid to rest, and then for no one. Beaten, it lets fall one of the things
// it wore (an artifact this game has already made is given as an ego of its kind instead), and gold. Not
// in the daily dungeon, the weekly challenge or the tutorial; a replay has only the ghosts its game had.
// One ghost in three (by its key and the game's seed, so no dice are rolled to choose) pleads instead,
// if what killed it is a monster that can still be met: it waits where it is, asks you when first seen to
// slay its killer — placed on the level out of sight if none of its kind is there — and, when one of that
// kind dies on the level, thanks you and is laid to rest, letting fall two or three of the things it wore.
// Struck, it fights after all; left behind, it may walk and plead again. (Not in a replay older than 3.)
public sealed partial class GameSession
{
    /// <summary>The fallen this character may meet as ghosts, those met, and those laid to rest (saved with it).</summary>
    public GhostLog Ghosts { get; private set; } = new();

    private readonly Dictionary<string, MonsterRaceDef> _ghostRaces = new(StringComparer.Ordinal);

    /// <summary>Whether ghosts walk for this character: the birth option, and not in the daily dungeon, the weekly challenge or the tutorial.</summary>
    public bool GhostsOn => Options[OptionIds.AvaGhosts] && !Player.IsSeededRun && !IsTutorial;

    /// <summary>
    /// The graveyard's fallen (newest first), handed in as the game is started or loaded — before its
    /// replay starts recording, so the replay begins with them. Those this character has met stay (one
    /// may be on a level kept in a persistent dungeon); this character itself is never among them.
    /// </summary>
    public void UseGraveyard(IEnumerable<FallenGhost> fallen)
    {
        var met = Ghosts.Fallen.Where(f => Ghosts.Met.Contains(f.Key) || Ghosts.Pleas.Any(p => p.Key == f.Key)).ToList();
        var self = $"{Seed:x}-";
        var fresh = !GhostsOn ? [] : fallen.Where(f => !(f.Name == Player.Name && f.Key.StartsWith(self, StringComparison.Ordinal))
                                                      && !Ghosts.Laid.Contains(f.Key)).Take(FallenGhosts.MaxKept).ToList();
        Ghosts.Fallen = [.. met, .. fresh.Where(f => met.All(m => m.Key != f.Key))];
    }

    /// <summary>Loading a save: its ghosts.</summary>
    internal void RestoreGhosts(GhostLog? log)
    {
        Ghosts = log ?? new GhostLog();
        _ghostRaces.Clear();
    }

    /// <summary>A race by id: the game data's, or a ghost's.</summary>
    public MonsterRaceDef? MonsterRace(string id) =>
        id.StartsWith(FallenGhosts.RacePrefix, StringComparison.Ordinal) ? GhostRace(id[FallenGhosts.RacePrefix.Length..]) : Data.Monster(id);

    /// <summary>The ghost of a fallen character this game knows of (by its headstone's key).</summary>
    private MonsterRaceDef? GhostRace(string key)
    {
        if (_ghostRaces.TryGetValue(key, out var race)) return race;
        if (Ghosts.Fallen.FirstOrDefault(f => f.Key == key) is not { } fallen) return null;
        return _ghostRaces[key] = FallenGhosts.BuildRace(Data, fallen);
    }

    /// <summary>The fallen character a ghost monster is, or null for any other monster.</summary>
    public FallenGhost? GhostOf(Monster monster)
    {
        var id = (monster.OriginalRace ?? monster.Race).Id;
        return id.StartsWith(FallenGhosts.RacePrefix, StringComparison.Ordinal)
            ? Ghosts.Fallen.FirstOrDefault(f => f.Key == id[FallenGhosts.RacePrefix.Length..]) : null;
    }

    /// <summary>
    /// A new level is made and peopled: perhaps a ghost of someone who fell at this depth. No dice are
    /// rolled unless one could walk here, so a game without them plays as before.
    /// </summary>
    private void PlaceGhost()
    {
        if (!GhostsOn || Level.Depth <= 0) return;
        var here = Ghosts.Fallen.Where(f => f.Depth == Level.Depth && !Ghosts.Met.Contains(f.Key) && !Ghosts.Laid.Contains(f.Key)).ToList();
        foreach (var fallen in here)
        {
            if (!Rng.OneIn(FallenGhosts.Chance)) continue;
            if (GhostSpot() is not { } at || GhostRace(fallen.Key) is not { } race) return;
            var ghost = _spawner.Place(Level, Rng, race, at);
            Scheduler.Add(ghost);
            Ghosts.Met.Add(fallen.Key);
            if (PleadingKiller(fallen) is { } killer)
            {
                var plea = PleaOf(fallen);
                if (plea is null) Ghosts.Pleas.Add(plea = new GhostPlea { Key = fallen.Key });
                (plea.Killer, plea.Stage) = (killer.Id, GhostPlea.Pleading);
                PlaceKiller(killer);
            }
            Publish(new MessageEvent("A chill runs through you. Someone who fell here does not rest."));
            return;
        }
    }

    /// <summary>Whether ghosts may plead (not when playing back a replay recorded before they could: it played without them).</summary>
    internal bool GhostPleasOff { get; set; }

    /// <summary>A ghost's plea, if it ever made one to this character.</summary>
    private GhostPlea? PleaOf(FallenGhost fallen) => Ghosts.Pleas.FirstOrDefault(p => p.Key == fallen.Key);

    /// <summary>
    /// What a ghost walking now would ask you to slay — null if it fights instead: one in three plead (the
    /// same ones every time in this game), and one that pleaded before and was left pleads again; never one
    /// you struck, nor one whose killer can't be found: not a monster, or a unique that is dead or kept on
    /// another level.
    /// </summary>
    private MonsterRaceDef? PleadingKiller(FallenGhost fallen)
    {
        if (GhostPleasOff) return null;
        var before = PleaOf(fallen);
        if (before is { Stage: not GhostPlea.Left } || before is null && !FallenGhosts.Pleads(fallen, Seed)) return null;
        if (FallenGhosts.KillerRace(Data, fallen.KilledBy) is not { } killer) return null;
        if (killer.IsUnique && (KilledUniques.Contains(killer.Id) || PersistentLevels && UniquesOnStoredLevels().Contains(killer.Id))) return null;
        return killer;
    }

    /// <summary>
    /// So the plea can be answered: unless one of its kind is already here, the ghost's killer somewhere out
    /// of your sight (a unique itself; anything else one of its kind, with a friend or two if it comes in packs).
    /// </summary>
    private void PlaceKiller(MonsterRaceDef killer)
    {
        if (Level.Monsters.All.Any(m => (m.OriginalRace ?? m.Race).Id == killer.Id) || GhostSpot() is not { } at) return;
        Scheduler.Add(_spawner.Place(Level, Rng, killer, at));
        if (killer.IsUnique || !killer.Friends.Any(f => f.IsSame)) return;
        var friends = Rng.RandInt1(2);
        foreach (var dir in DirectionExtensions.Compass)
        {
            if (friends == 0) break;
            if (at.Step(dir) is var p && Level.InBounds(p) && Level.IsEmptyFloor(p) && p != Player.Position)
            {
                Scheduler.Add(_spawner.Place(Level, Rng, killer, p));
                friends--;
            }
        }
    }

    /// <summary>Whether a monster is a ghost waiting for you to avenge it (it neither moves nor fights meanwhile).</summary>
    public bool IsPleading(Monster monster) => GhostOf(monster) is { } fallen && PleaOf(fallen) is { Stage: GhostPlea.Pleading };

    /// <summary>The level left: a pleading ghost left behind fades, and may walk and plead another time (unless the level is kept, and it with it).</summary>
    private void GhostsLeftBehind(bool kept)
    {
        if (kept) return;
        foreach (var plea in Ghosts.Pleas.Where(p => p.Stage == GhostPlea.Pleading))
        {
            plea.Stage = GhostPlea.Left;
            Ghosts.Met.Remove(plea.Key);
        }
    }

    /// <summary>A pleading ghost struck (by you or your hireling): it pleads no more, and fights.</summary>
    private void GhostSpurned(Monster monster)
    {
        if (GhostOf(monster) is not { } fallen || PleaOf(fallen) is not { Stage: GhostPlea.Pleading } plea) return;
        plea.Stage = GhostPlea.Spurned;
        Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} shrieks: \"Then you are no better than {fallen.KilledBy}!\""));
    }

    /// <summary>
    /// A monster slain: if it was of the race that killed a ghost pleading on this level, the ghost is
    /// avenged — it thanks you and is laid to rest, letting fall two or three of the things it wore, and gold.
    /// </summary>
    private void GhostAvenged(Monster killed)
    {
        if (Ghosts.Pleas.Count == 0) return;
        var raceId = (killed.OriginalRace ?? killed.Race).Id;
        foreach (var plea in Ghosts.Pleas.Where(p => p.Stage == GhostPlea.Pleading && p.Killer == raceId).ToList())
        {
            var ghost = Level.Monsters.All.FirstOrDefault(m => GhostOf(m)?.Key == plea.Key);
            if (ghost is null || GhostOf(ghost) is not { } fallen) continue;
            plea.Stage = GhostPlea.Avenged;
            Ghosts.Laid.Add(fallen.Key);
            var thanks = FallenGhosts.Thanks(fallen);
            Publish(new MessageEvent($"{Capitalize(MonsterName(ghost))} whispers: \"{thanks}\""));
            Publish(new MessageEvent($"{fallen.Name} the {fallen.Race} {fallen.Class} is laid to rest at last."));
            Level.Monsters.Remove(ghost);
            var level = FallenGhosts.LevelOf(fallen);
            foreach (var item in GhostRewards(fallen, level, 2 + Rng.RandInt0(2)))
            {
                Publish(new MessageEvent($"As it fades, the ghost lets fall {Describe(item)}."));
                DropNear(item, ghost.Position);
            }
            for (var i = 0; i < 3; i++) DropNear(MakeLevelGold(level), ghost.Position);
            AddHistory($"Avenged the ghost of {fallen.Name}");
            Publish(new GhostAvengedEvent(ghost, fallen, thanks));
            Publish(new GhostLaidToRestEvent(fallen.Key, fallen.Name));
        }
    }

    /// <summary>Where a ghost starts: empty floor well away from you, out of your sight, and not in a vault.</summary>
    private Loc? GhostSpot()
    {
        var sight = Data.Constants.MaxSight;
        var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && !Level[p].Has(SquareFlags.Vault) && p.DistanceTo(Player.Position) > 10
                                               && !ProjectionPath.Projectable(Level, Player.Position, p, sight)).ToList();
        return spots.Count == 0 ? null : spots[Rng.RandInt0(spots.Count)];
    }

    /// <summary>A ghost seen for the first time: it wails — or, pleading, asks you to slay what killed it (the quest log keeps it).</summary>
    private void GhostSighted(Monster monster)
    {
        if (GhostOf(monster) is not { } fallen) return;
        if (PleaOf(fallen) is { Stage: GhostPlea.Pleading } plea)
        {
            var words = FallenGhosts.Plea(fallen);
            plea.Spoken = true;
            Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} pleads: \"{words}\""));
            Publish(new MessageEvent($"Slay {fallen.KilledBy} on this level to lay the ghost to rest (your quest log has it)."));
            Publish(new GhostSightedEvent(monster, fallen, words, Pleading: true));
            return;
        }
        var wail = FallenGhosts.Wail(fallen);
        Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wails: \"{wail}\""));
        Publish(new GhostSightedEvent(monster, fallen, wail, Pleading: false));
    }

    /// <summary>A ghost beaten: laid to rest, it lets fall one of the things it wore, and gold.</summary>
    private void GhostKilled(Monster monster)
    {
        if (GhostOf(monster) is not { } fallen) return;
        var level = FallenGhosts.LevelOf(fallen);
        Ghosts.Laid.Add(fallen.Key);
        Publish(new MessageEvent($"{fallen.Name} the {fallen.Race} {fallen.Class} is laid to rest at last."));
        foreach (var item in GhostRewards(fallen, level, 1))
        {
            Publish(new MessageEvent($"As it fades, the ghost lets fall {Describe(item)}."));
            DropNear(item, monster.Position);
        }
        for (var i = 0; i < 2; i++) DropNear(MakeLevelGold(level), monster.Position);
        Publish(new GhostLaidToRestEvent(fallen.Key, fallen.Name));
    }

    /// <summary>
    /// What a ghost lets fall: <paramref name="count"/> of the things it wore, at random, each once (never a
    /// quest item or a wooden torch, nor anything the game no longer has). An artifact comes as itself only if
    /// this game hasn't made it and both games use the standard set; otherwise as an ego of its kind. A
    /// headstone without its gear (or without enough of it) gives great items suited to the class instead.
    /// </summary>
    private List<Item> GhostRewards(FallenGhost fallen, int level, int count)
    {
        var worn = new List<(string Json, ObjectKindDef Kind, string? Artifact)>();
        foreach (var json in fallen.Equipment ?? [])
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            if (node?["Kind"]?.GetValue<string>() is not { } kindId || Data.Object(kindId) is not { } kind || kind.Id == "wooden_torch"
                || node["QuestTag"]?.GetValue<string>() is not null) continue;
            worn.Add((json, kind, node["Artifact"]?.GetValue<string>()));
        }
        var rewards = new List<Item>();
        for (var i = 0; i < count; i++)
        {
            if (worn.Count == 0)
            {
                var classId = FallenGhosts.ClassOf(Data, fallen)?.Id ?? "warrior";
                if (classId.StartsWith(Multiclass.Fighter + "_", StringComparison.Ordinal)) classId = Multiclass.Fighter;
                if (Objects.Make(Rng, level, good: true, great: true, bases: FallenGhosts.RewardBases(classId)) is { } great) rewards.Add(great);
                continue;
            }
            var pick = Rng.RandInt0(worn.Count);
            if (GhostReward(fallen, level, worn[pick]) is { } item) rewards.Add(item);
            worn.RemoveAt(pick);
        }
        return rewards;
    }

    /// <summary>One thing a ghost wore, as it lets it fall (an artifact it can't give, as an ego of its kind).</summary>
    private Item? GhostReward(FallenGhost fallen, int level, (string Json, ObjectKindDef Kind, string? Artifact) worn)
    {
        var (itemJson, itemKind, artifact) = worn;
        if (artifact is not null)
        {
            var standard = !fallen.Randarts && RandartSeed is null && Data.Artifacts.Any(a => a.Id == artifact);
            if (!standard || !Objects.AllowArtifacts || Objects.CreatedArtifacts.Contains(artifact)) return GhostEgo(itemKind, level);
        }
        if (SaveGame.ItemFromJson(this, itemJson) is not { } item) return GhostEgo(itemKind, level);
        if (item.Artifact is { } art) Objects.CreatedArtifacts.Add(art.Id);
        item.Note = null; // (its inscription was the fallen character's)
        item.Ignored = false;
        return item;
    }

    /// <summary>An ego of a kind, in place of an artifact the ghost can't give (a kind only ever an artifact: a great item of its base).</summary>
    private Item? GhostEgo(ObjectKindDef kind, int level)
    {
        if (kind.IsSpecialArtifactKind) return Objects.Make(Rng, level, good: true, great: true, bases: [kind.Base]);
        var item = Objects.Create(kind);
        item.OriginDepth = level;
        Objects.ApplyMagic(Rng, item, level, good: true, great: true, artifacts: false);
        return item;
    }
}
