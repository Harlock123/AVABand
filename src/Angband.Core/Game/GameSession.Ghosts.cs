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
}

/// <summary>A fallen character's ghost laid to rest (the app marks its headstone; <paramref name="Key"/> is the headstone's).</summary>
public sealed record GhostLaidToRestEvent(string Key, string Name) : IGameEvent;

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
        var killer = g.KilledBy.Length == 0 ? "Something" : char.ToUpperInvariant(g.KilledBy[0]) + g.KilledBy[1..];
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
// in the daily dungeon or the tutorial; a replay has only the ghosts its game had.
public sealed partial class GameSession
{
    /// <summary>The fallen this character may meet as ghosts, those met, and those laid to rest (saved with it).</summary>
    public GhostLog Ghosts { get; private set; } = new();

    private readonly Dictionary<string, MonsterRaceDef> _ghostRaces = new(StringComparer.Ordinal);

    /// <summary>Whether ghosts walk for this character: the birth option, and not in the daily dungeon or the tutorial.</summary>
    public bool GhostsOn => Options[OptionIds.AvaGhosts] && Player.DailyDate is null && !IsTutorial;

    /// <summary>
    /// The graveyard's fallen (newest first), handed in as the game is started or loaded — before its
    /// replay starts recording, so the replay begins with them. Those this character has met stay (one
    /// may be on a level kept in a persistent dungeon); this character itself is never among them.
    /// </summary>
    public void UseGraveyard(IEnumerable<FallenGhost> fallen)
    {
        var met = Ghosts.Fallen.Where(f => Ghosts.Met.Contains(f.Key)).ToList();
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
            Publish(new MessageEvent("A chill runs through you. Someone who fell here does not rest."));
            return;
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

    /// <summary>A ghost seen for the first time: it wails.</summary>
    private void GhostSighted(Monster monster)
    {
        if (GhostOf(monster) is { } fallen)
            Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wails: \"{FallenGhosts.Wail(fallen)}\""));
    }

    /// <summary>A ghost beaten: laid to rest, it lets fall one of the things it wore, and gold.</summary>
    private void GhostKilled(Monster monster)
    {
        if (GhostOf(monster) is not { } fallen) return;
        var level = FallenGhosts.LevelOf(fallen);
        Ghosts.Laid.Add(fallen.Key);
        Publish(new MessageEvent($"{fallen.Name} the {fallen.Race} {fallen.Class} is laid to rest at last."));
        if (GhostReward(fallen, level) is { } item)
        {
            Publish(new MessageEvent($"As it fades, the ghost lets fall {Describe(item)}."));
            DropNear(item, monster.Position);
        }
        for (var i = 0; i < 2; i++) DropNear(MakeLevelGold(level), monster.Position);
        Publish(new GhostLaidToRestEvent(fallen.Key, fallen.Name));
    }

    /// <summary>
    /// What a ghost lets fall: one of the things it wore, at random (never a quest item or a wooden torch,
    /// nor anything the game no longer has). An artifact comes as itself only if this game hasn't made it
    /// and both games use the standard set; otherwise as an ego of its kind. A headstone without its gear
    /// gives a great item suited to the class instead.
    /// </summary>
    private Item? GhostReward(FallenGhost fallen, int level)
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
        if (worn.Count == 0)
        {
            var classId = FallenGhosts.ClassOf(Data, fallen)?.Id ?? "warrior";
            if (classId.StartsWith(Multiclass.Fighter + "_", StringComparison.Ordinal)) classId = Multiclass.Fighter;
            return Objects.Make(Rng, level, good: true, great: true, bases: FallenGhosts.RewardBases(classId));
        }
        var (itemJson, itemKind, artifact) = worn[Rng.RandInt0(worn.Count)];
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
