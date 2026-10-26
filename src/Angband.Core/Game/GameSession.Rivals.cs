using System.Text.Json.Serialization;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Randomness;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>How a rival band takes to you: decided at birth, and hostile for good once you attack them.</summary>
public enum RivalTemper { Friendly, Wary, Hostile }

/// <summary>One of a rival band (AVABand's own; kept in the save).</summary>
public sealed class RivalMember
{
    public string Name { get; set; } = "";
    public string RaceId { get; set; } = "";
    /// <summary>The race's name, as the game had it when the band was made ("Dwarf").</summary>
    public string Race { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string Class { get; set; } = "";
    public bool Dead { get; set; }
    /// <summary>How they fell ("slain by Bramble at 500 ft"); null while they live.</summary>
    public string? Fate { get; set; }
    /// <summary>The one thing they carry to trade (the save file's form of the item); null for nothing.</summary>
    public string? Carries { get; set; }
    /// <summary>What they ask for it.</summary>
    public long Price { get; set; }
    /// <summary>The level their body was made at when last met (their race is made from it, so a save gives the same body).</summary>
    public int MetLevel { get; set; }

    /// <summary>"Brannor the Dwarf Warrior".</summary>
    [JsonIgnore]
    public string Title => $"{Name} the {Race} {Class}";
}

/// <summary>Something a rival band did, as the news tells it ("Brannor's band slew Grip, Farmer Maggot's Dog at 100 ft").</summary>
public sealed class RivalDeed
{
    public long Turn { get; set; }
    /// <summary>Where it happened (0 for nowhere in particular).</summary>
    public int Depth { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Already told in town.</summary>
    public bool Told { get; set; }
}

/// <summary>
/// A character's rival adventurers (AVABand's own; kept in the save): a named leader and two
/// companions, made at birth, who go down the dungeon alongside you, off-screen, and are now and then met.
/// </summary>
public sealed class RivalBand
{
    /// <summary>The leader first.</summary>
    public List<RivalMember> Members { get; set; } = [];
    public RivalTemper Temper { get; set; }
    /// <summary>You attacked them while they were at peace with you: hostile for good.</summary>
    public bool Provoked { get; set; }
    /// <summary>How deep they have got.</summary>
    public int Depth { get; set; } = 1;
    /// <summary>What they have done, the first first.</summary>
    public List<RivalDeed> Deeds { get; set; } = [];
    /// <summary>How many times you have met them.</summary>
    public int Meetings { get; set; }
    /// <summary>New levels since you last met them (they keep their distance for a while after a meeting).</summary>
    public int LevelsSinceMeeting { get; set; } = RivalBands.MeetingGap;

    /// <summary>"Brannor's band" (named for the leader, alive or not).</summary>
    [JsonIgnore]
    public string Name => Members.Count == 0 || Members[0].Name.Length == 0 ? "A rival band" : $"{Members[0].Name}'s band";

    /// <summary>All of them dead: the band is no more.</summary>
    [JsonIgnore]
    public bool IsGone => Members.All(m => m.Dead);

    [JsonIgnore]
    public bool IsHostile => Temper == RivalTemper.Hostile || Provoked;
}

/// <summary>
/// AVABand's rival adventurers: how a band is made at birth, the monster each of them is when met,
/// and which uniques and artifacts the band may claim before you.
/// </summary>
public static class RivalBands
{
    /// <summary>What a rival's race id starts with (then their place in the band).</summary>
    public const string RacePrefix = "rival:";

    /// <summary>The fewest new levels between two meetings.</summary>
    public const int MeetingGap = 10;

    /// <summary>One new level in this many (near their depth, after the gap) has the band, or one of them, on it.</summary>
    public const int MeetingChance = 12;

    /// <summary>How far from their depth they may be met.</summary>
    public const int MeetingRange = 3;

    /// <summary>A unique the band may slay is at least this many levels shallower than your deepest.</summary>
    public const int OutgrownBy = 8;

    /// <summary>The deepest-made artifact the band may take first (only modest ones).</summary>
    public const int ModestArtifactLevel = 30;

    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["human"] = ["Aldric", "Berengar", "Corwin", "Edric", "Gavran", "Hallam", "Merrick", "Osric", "Rowan", "Tamsin", "Wulfric", "Ysolde"],
        ["half_elf"] = ["Aerin", "Celebor", "Elwen", "Faelan", "Ithil", "Maelor", "Nimeth", "Talan"],
        ["elf"] = ["Aranel", "Caladir", "Elrohan", "Glorwen", "Haldir", "Lorindol", "Mithrel", "Sindarel"],
        ["high_elf"] = ["Aegnoron", "Calenmir", "Elendur", "Finrodel", "Glorfin", "Turgonel"],
        ["hobbit"] = ["Bungo", "Drogo", "Fosco", "Largo", "Milo", "Posco", "Rorimac", "Tobold"],
        ["gnome"] = ["Bimble", "Fizwick", "Nackle", "Quillon", "Tinkerby", "Wobbin"],
        ["dwarf"] = ["Brannor", "Durgin", "Farin", "Grimnar", "Kili", "Norri", "Thrain", "Vorin"],
        ["half_orc"] = ["Gashna", "Grukk", "Morzag", "Shagrat", "Ugbash", "Zogra"],
        ["half_troll"] = ["Brugg", "Grumm", "Hurk", "Murg", "Thokk", "Ulgar"],
        ["dunadan"] = ["Arahad", "Belegorn", "Dirhael", "Halbaron", "Ivorwen", "Mardil"],
        ["kobold"] = ["Grizz", "Kekk", "Rikkit", "Skitt", "Yipp", "Zazz"],
    };

    private static readonly string[] AnyNames = ["Arlen", "Brisk", "Cador", "Dain", "Ewan", "Fenn", "Garr", "Hob"];

    /// <summary>
    /// A band for a new character: three of the game's races and classes, named, with a temper (one in
    /// two friendly, three in ten wary, one in five hostile). Made from dice of its own, seeded from the
    /// game, so nothing else in the game shifts.
    /// </summary>
    public static RivalBand Make(GameData data, ulong seed)
    {
        var dice = new GameRandom(seed ^ 0x5249_5641_4C53UL); // ("RIVALS")
        var band = new RivalBand();
        var roll = dice.RandInt0(10);
        band.Temper = roll < 5 ? RivalTemper.Friendly : roll < 8 ? RivalTemper.Wary : RivalTemper.Hostile;
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 3; i++)
        {
            var race = data.Races.Count == 0 ? null : data.Races[dice.RandInt0(data.Races.Count)];
            var cls = data.Classes.Count == 0 ? null : data.Classes[dice.RandInt0(data.Classes.Count)];
            var names = race is not null && Names.TryGetValue(race.Id, out var list) ? list : AnyNames;
            var name = names[dice.RandInt0(names.Length)];
            for (var tries = 0; !taken.Add(name) && tries < 20; tries++) name = names[dice.RandInt0(names.Length)];
            band.Members.Add(new RivalMember
            {
                Name = name, RaceId = race?.Id ?? "human", Race = race?.Name ?? "Human", ClassId = cls?.Id ?? "warrior", Class = cls?.Name ?? "Warrior",
            });
        }
        return band;
    }

    /// <summary>The level a rival's body is made at, met on a level of this depth: the band's depth, or near it (never below 5).</summary>
    public static int LevelAt(RivalBand band, int depth) => Math.Clamp(Math.Max(band.Depth, depth) + 2, 5, 90);

    /// <summary>
    /// The monster a rival is: a person ('p', in their class's colour) of their level, unique, with blows
    /// and (for a caster) spells of their class as the ghosts of the fallen have them, and the picture of
    /// that class's kind of person. The leader is a little tougher.
    /// </summary>
    public static MonsterRaceDef BuildRace(GameData data, RivalBand band, int index)
    {
        var m = band.Members.ElementAtOrDefault(index) ?? new RivalMember { Name = "a rival" };
        var level = Math.Clamp(m.MetLevel > 0 ? m.MetLevel : LevelAt(band, band.Depth), 1, 100);
        var classId = data.Class(m.ClassId) is { } cls && !cls.Id.StartsWith(Multiclass.Fighter + "_", StringComparison.Ordinal) ? cls.Id : "warrior";
        var fighter = classId is "warrior" or "blackguard" or "paladin";
        var skirmisher = classId is "rogue" or "ranger";

        var hp = (10 * level + level * level / 2) * (fighter ? 6 : skirmisher ? 5 : 4) / 5;
        if (index == 0) hp = hp * 5 / 4;
        var blows = fighter
            ? Enumerable.Repeat(new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = new Dice(1 + level / 10, 8) }, 3)
            : skirmisher
                ? Enumerable.Repeat(new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = new Dice(1 + level / 10, 6) }, 2)
                : [new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = new Dice(1 + level / 12, 5) }];
        var (attacks, others) = FallenGhosts.SpellsOf(classId);
        var spells = attacks.Where(s => s.Level <= level).TakeLast(2)
            .Concat(others.Where(s => s.Level <= level).TakeLast(2)).Select(s => s.Spell).ToList();
        if (classId == "ranger") spells.Add("ARROW");
        var flags = new HashSet<string>(StringComparer.Ordinal) { MonsterFlags.Unique, MonsterFlags.OpenDoor, MonsterFlags.BashDoor };
        if (spells.Count > 0 && !fighter && !skirmisher) flags.Add(MonsterFlags.Smart);
        var feet = band.Depth * data.Constants.FeetPerLevel;
        return new MonsterRaceDef
        {
            Id = RacePrefix + index,
            Name = m.Name,
            Glyph = 'p',
            Base = "person",
            Color = FallenGhosts.Color(classId),
            Depth = level,
            Rarity = 1,
            Speed = Math.Min(15, level / 6),
            HitPoints = Math.Max(10, hp),
            Armour = (fighter ? 12 : 8) + level * 3 / 2,
            Sleep = 0,
            Hearing = 20,
            SpellFrequency = spells.Count == 0 ? 0 : fighter || skirmisher ? 6 : 3,
            InnateFrequency = classId == "ranger" ? 4 : 0,
            Spells = spells,
            Experience = level * 12,
            Blows = [.. blows],
            Flags = flags,
            TileAs = data.Monster(classId) is not null ? classId : null,
            Description = $"{m.Title}, {(index == 0 ? "who leads" : "one of")} {band.Name}: rival adventurers, last heard of about {feet} ft down.",
        };
    }

    /// <summary>
    /// The uniques the band may slay: alive, out of the town, no deeper than the band has got and well
    /// shallower than your deepest (<see cref="OutgrownBy"/>); never a quest's own (Sauron, Morgoth and
    /// AVABand's quest monsters) nor one a notice-board bounty (or the WANTED poster) is on.
    /// </summary>
    public static bool MaySlay(MonsterRaceDef race, RivalBand band, int maxDepth) =>
        race.IsUnique && !race.Has(MonsterFlags.Questor) && race.Depth >= 1 && race.Depth <= band.Depth && race.Depth <= maxDepth - OutgrownBy
        && !race.Id.StartsWith(RacePrefix, StringComparison.Ordinal) && !race.Id.StartsWith(FallenGhosts.RacePrefix, StringComparison.Ordinal);

    /// <summary>The artifacts the band may take first: modest ones (made no deeper than <see cref="ModestArtifactLevel"/>, nor than the band has got), not one only ever made as itself.</summary>
    public static bool MayTake(GameData data, ArtifactDef art, RivalBand band) =>
        art.Level >= 1 && art.Level <= Math.Min(ModestArtifactLevel, band.Depth) && data.Object(art.Kind) is { IsSpecialArtifactKind: false };

    /// <summary>"the Dagger 'Narthanc'".</summary>
    public static string ArtifactName(GameData data, ArtifactDef art) =>
        $"the {(data.Object(art.Kind) is { } kind ? ItemNaming.Plain(kind.Name, false) + " " : "")}{art.Name}";
}

// AVABand's own: rival adventurers (the birth option birth_ava_rivals). Each character has one rival
// band, made at birth — a named leader and two companions, each of a race and a class — that goes down
// the dungeon alongside you, off-screen: each time you come back up to town they have got a little
// deeper (a little behind your deepest, or a little ahead), and the news says what they did. They may
// slay a unique you have long outgrown (it is dead in this game) and, rarely, take a modest artifact
// before you (it won't be found). Now and then they are met on a level, as 'p' monsters: a friendly
// band will sell you the one thing each carries, a wary one passes by, a hostile one attacks. Attack a
// friendly band and they are hostile for good; kill one and they drop what they carried, and the news
// tells it; with all three dead the band is gone. Their dice are their own (seeded from the game, the
// time or the level), so a game without them plays as before. Never in the daily dungeon, the weekly
// challenge or the tutorial; characters made before it have no band.
public sealed partial class GameSession
{
    /// <summary>This character's rival band (null for one made without it, or before it existed).</summary>
    public RivalBand? Rivals { get; private set; }

    private readonly Dictionary<int, MonsterRaceDef> _rivalRaces = [];

    /// <summary>Whether rival adventurers are about: the birth option and a band, never in the daily dungeon, the weekly challenge or the tutorial.</summary>
    public bool RivalsOn => Rivals is not null && Options[OptionIds.AvaRivals] && !Player.IsSeededRun && !IsTutorial;

    /// <summary>A new character: its rival band (with the option on).</summary>
    private void MakeRivals()
    {
        if (Options[OptionIds.AvaRivals]) Rivals = RivalBands.Make(Data, Seed);
    }

    /// <summary>Loading a save: its rival band, if it has one.</summary>
    internal void RestoreRivals(RivalBand? band)
    {
        Rivals = band;
        _rivalRaces.Clear();
    }

    /// <summary>Gives this game a rival band (tests).</summary>
    internal void UseRivals(RivalBand? band) => RestoreRivals(band);

    /// <summary>The race a rival's body is (by their place in the band); null if there's no such rival.</summary>
    private MonsterRaceDef? RivalRace(string index)
    {
        if (Rivals is not { } band || !int.TryParse(index, out var i) || i < 0 || i >= band.Members.Count) return null;
        if (_rivalRaces.TryGetValue(i, out var race)) return race;
        return _rivalRaces[i] = RivalBands.BuildRace(Data, band, i);
    }

    /// <summary>The rival a monster is (and their place in the band), or null for any other monster.</summary>
    public (RivalMember Member, int Index)? RivalOf(Monster monster)
    {
        var id = (monster.OriginalRace ?? monster.Race).Id;
        if (Rivals is not { } band || !id.StartsWith(RivalBands.RacePrefix, StringComparison.Ordinal)
            || !int.TryParse(id.AsSpan(RivalBands.RacePrefix.Length), out var i) || i < 0 || i >= band.Members.Count) return null;
        return (band.Members[i], i);
    }

    /// <summary>A rival at peace with you (friendly or wary, and not yet attacked).</summary>
    public bool RivalAtPeace(Monster monster) => Rivals is { IsHostile: false } && RivalOf(monster) is not null;

    private void AddRivalDeed(string text, int depth, bool told = false) =>
        Rivals?.Deeds.Add(new RivalDeed { Turn = GameTurn, Depth = depth, Text = text, Told = told });

    private string Feet(int depth) => $"{depth * Data.Constants.FeetPerLevel} ft";

    // --- Off-screen: the news ----------------------------------------------------------------------

    /// <summary>
    /// Back in town from the dungeon: the band has gone on — deeper, near your deepest — and may have
    /// slain a unique you outgrew (one time in three) or, rarely, taken a modest artifact (one in
    /// twelve). Then the news not yet told.
    /// </summary>
    internal void RivalsOnReturn()
    {
        if (!RivalsOn || Rivals is not { } band) return;
        if (!band.IsGone)
        {
            var dice = new GameRandom(Seed ^ 0x4E_45_57_53UL ^ (ulong)GameTurn * 0x9E3779B97F4A7C15UL);
            var target = Math.Clamp(Player.MaxDepth + dice.RandRange(-3, 2), 1, 99);
            if (target > band.Depth)
            {
                var before = band.Depth;
                band.Depth = Math.Min(target, band.Depth + 5);
                if (band.Depth / 10 > before / 10) AddRivalDeed($"{band.Name} has gone down past {Feet(band.Depth / 10 * 10)}", band.Depth);
            }
            if (dice.OneIn(3) && RivalUniqueToSlay(dice) is { } unique)
            {
                KilledUniques.Add(unique.Id);
                AddRivalDeed($"{band.Name} slew {unique.Name} at {Feet(unique.Depth)}", unique.Depth);
            }
            if (dice.OneIn(12) && Objects.AllowArtifacts)
            {
                var arts = Objects.Artifacts.Where(a => !Objects.CreatedArtifacts.Contains(a.Id) && RivalBands.MayTake(Data, a, band)).ToList();
                if (arts.Count > 0)
                {
                    var art = arts[dice.RandInt0(arts.Count)];
                    Objects.CreatedArtifacts.Add(art.Id);
                    AddRivalDeed($"{band.Name} found {RivalBands.ArtifactName(Data, art)}", art.Level);
                }
            }
        }
        foreach (var deed in band.Deeds.Where(d => !d.Told))
        {
            deed.Told = true;
            Publish(new MessageEvent($"They say {deed.Text}."));
        }
    }

    /// <summary>A unique the band may slay now (<see cref="RivalBands.MaySlay"/>), not one alive on a level you keep; null for none.</summary>
    private MonsterRaceDef? RivalUniqueToSlay(GameRandom dice)
    {
        if (Rivals is not { } band) return null;
        var unavailable = new HashSet<string>(KilledUniques);
        if (PersistentLevels) unavailable.UnionWith(UniquesOnStoredLevels());
        foreach (var m in Level.Monsters.All) unavailable.Add(m.Race.Id);
        var bounties = AvaQuests.Board.Where(j => j.Kind == "bounty").Select(j => j.Target).ToHashSet(StringComparer.Ordinal);
        if (AvaQuests.Bounty is { } poster) bounties.Add(poster.Race); // (the WANTED poster's too)
        var uniques = Data.Monsters.Where(r => RivalBands.MaySlay(r, band, Player.MaxDepth) && !unavailable.Contains(r.Id) && !bounties.Contains(r.Id))
            .ToList();
        return uniques.Count == 0 ? null : uniques[dice.RandInt0(uniques.Count)];
    }

    /// <summary>At the Prancing Pony: the latest word of the band.</summary>
    private void RivalNewsAtInn()
    {
        if (!RivalsOn || Rivals is not { } band) return;
        var latest = band.Deeds.LastOrDefault();
        Publish(new MessageEvent(latest is null
            ? $"Butterbur polishes a mug. \"No word of {band.Name} lately. Last I heard they were about {Feet(band.Depth)} down.\""
            : $"Butterbur leans over the bar. \"They say {latest.Text}.\""));
        if (latest is not null) latest.Told = true;
    }

    // --- Meeting them ------------------------------------------------------------------------------

    /// <summary>
    /// A new level is made and peopled: now and then — near the band's depth, a while since you last met
    /// them, once you have been 250 ft down — the band is here, or one of them (one time in three).
    /// They start awake, together, out of sight of where you arrive. The dice are the level's own.
    /// </summary>
    internal void PlaceRivals()
    {
        if (!RivalsOn || Rivals is not { } band || band.IsGone || Level.Depth <= 0 || QuestAt(Level.Depth) is not null) return;
        band.LevelsSinceMeeting++;
        if (band.LevelsSinceMeeting < RivalBands.MeetingGap || Player.MaxDepth < 5 || Math.Abs(Level.Depth - band.Depth) > RivalBands.MeetingRange) return;
        var dice = new GameRandom(Level.Seed ^ 0x4D_45_45_54UL);
        if (!dice.OneIn(RivalBands.MeetingChance)) return;
        // (Not one whose body is on a level kept in a persistent dungeon.)
        var kept = PersistentLevels ? UniquesOnStoredLevels().ToHashSet(StringComparer.Ordinal) : [];
        var alive = band.Members.Select((m, i) => (Member: m, Index: i))
            .Where(x => !x.Member.Dead && !kept.Contains(RivalBands.RacePrefix + x.Index)).ToList();
        if (alive.Count == 0) return;
        if (dice.OneIn(3)) alive = [alive[dice.RandInt0(alive.Count)]];
        if (RivalSpot(dice) is not { } start) return;
        var placed = 0;
        foreach (var (_, index) in alive)
        {
            var at = placed == 0 ? start
                : Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p.ChebyshevTo(start) <= 3 && p != Player.Position)
                    .OrderBy(p => p.DistanceTo(start)).ThenBy(p => p.Y).ThenBy(p => p.X).Select(p => (Loc?)p).FirstOrDefault();
            if (at is not { } spot) break;
            if (MeetRival(index, spot, dice) is not null) placed++;
        }
        if (placed == 0) return;
        band.LevelsSinceMeeting = 0;
        band.Meetings++;
    }

    /// <summary>One of the band on this level, at a spot (awake; aware of you if hostile), with something to trade if they had nothing.</summary>
    internal Monster? MeetRival(int index, Loc at, GameRandom dice)
    {
        if (Rivals is not { } band || index < 0 || index >= band.Members.Count || band.Members[index].Dead) return null;
        var member = band.Members[index];
        var level = RivalBands.LevelAt(band, Level.Depth);
        member.MetLevel = level;
        _rivalRaces.Remove(index);
        if (member.Carries is null) RivalWares(member, dice, level);
        if (RivalRace(index.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not { } race) return null;
        var body = _spawner.Place(Level, dice, race, at, asleep: false);
        body.Aware = band.IsHostile;
        Scheduler.Add(body);
        return body;
    }

    /// <summary>Where the band starts: empty floor well away from you, out of your sight, and not in a vault.</summary>
    private Loc? RivalSpot(GameRandom dice)
    {
        var sight = Data.Constants.MaxSight;
        var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && !Level[p].Has(SquareFlags.Vault) && p.DistanceTo(Player.Position) > 10
                                               && !Combat.ProjectionPath.Projectable(Level, Player.Position, p, sight)).ToList();
        return spots.Count == 0 ? null : spots[dice.RandInt0(spots.Count)];
    }

    /// <summary>
    /// The one thing a rival carries to trade: something good of their level — never gold, an artifact
    /// or anything cursed — at twice what it is worth. Made aside (the serials and any artifact are put
    /// back), so nothing else in the game moves.
    /// </summary>
    private void RivalWares(RivalMember member, GameRandom dice, int level)
    {
        var serial = Objects.NextSerial;
        var artifacts = Objects.CreatedArtifacts.ToHashSet(StringComparer.Ordinal);
        try
        {
            for (var tries = 0; tries < 40; tries++)
            {
                if (Objects.Make(dice, level, good: true) is not { } item || item.IsGold || item.IsArtifact || item.IsCursed || item.IsQuestItem) continue;
                member.Carries = SaveGame.ItemToJson(item);
                member.Price = Math.Max(50, ItemValue.Of(item, Data) * item.Number * 2);
                return;
            }
        }
        finally
        {
            Objects.NextSerial = serial;
            Objects.CreatedArtifacts.RemoveWhere(a => !artifacts.Contains(a));
        }
    }

    /// <summary>A rival seen for the first time on this level: they speak — a greeting, a warning or a threat.</summary>
    private void RivalSighted(Monster monster)
    {
        if (!RivalsOn || Rivals is not { } band || RivalOf(monster) is not { } rival) return;
        var (member, index) = rival;
        // (The first of the band here speaks for them; the others only nod.)
        var first = !Level.Monsters.All.Any(m => m != monster && m.EverSeen && RivalOf(m) is { Index: var j } && j != index);
        var you = Player.Name.Length > 0 ? Player.Name : "stranger";
        string line;
        if (band.IsHostile)
            line = first ? $"{member.Name} of {band.Name} draws steel: \"{you}! You should have stayed in town.\""
                : $"{member.Name} of {band.Name} snarls at you.";
        else if (band.Temper == RivalTemper.Wary)
            line = first ? $"{member.Name} of {band.Name} keeps a hand on their weapon: \"Keep your distance, {you}. We want no trouble.\""
                : $"{member.Name} of {band.Name} eyes you warily.";
        else
            line = $"{member.Name} of {band.Name} calls out: \"Well met, {you}!"
                   + (RivalItem(member) is { } item ? $" I've {Describe(item)} to spare, if you've the gold — come and talk.\"" : " Good hunting.\"");
        Publish(new MessageEvent(line));
        Disturb();
    }

    /// <summary>What a rival carries to trade, as an item of this game (with fresh serials); null for nothing (or something the game no longer has).</summary>
    private Item? RivalItem(RivalMember member) => member.Carries is { } json ? SaveGame.ItemFromJson(this, json) : null;

    /// <summary>
    /// Walking into a rival at peace with you: a word with them (no time passes). A friendly one offers
    /// what they carry; either way you may let them pass — or attack them.
    /// </summary>
    private void TalkToRival(Monster monster)
    {
        if (Rivals is not { } band || RivalOf(monster) is not { } rival) return;
        var (member, index) = rival;
        var choices = new List<(string, string)>();
        string text;
        if (band.Temper == RivalTemper.Friendly && RivalItem(member) is { } item)
        {
            text = $"{member.Title} unwraps {Describe(item)}. \"{member.Price} gold, and it's yours.\" You have {Player.Gold} gold."
                   + (NoShopping ? " (You swore off shopping: you can look, but not buy.)" : "");
            choices.Add(($"rival:buy:{index}", $"Buy it for {member.Price} gold"));
            choices.Add(("rival:no", "No, thank you"));
        }
        else
        {
            text = band.Temper == RivalTemper.Friendly ? $"{member.Title} grins. \"Nothing left to trade, I'm afraid. Good hunting!\""
                : $"{member.Title} steps back, hand on their weapon. \"We go our way, you go yours.\"";
            choices.Add(("rival:no", "Let them pass"));
        }
        choices.Add(($"rival:attack:{index}", "Attack them"));
        AskQuest(band.Name, text, [.. choices]);
    }

    private void RivalChoice(string[] parts)
    {
        if (Rivals is not { } band || !int.TryParse(parts.ElementAtOrDefault(2), out var i) || i < 0 || i >= band.Members.Count) return;
        var member = band.Members[i];
        switch (parts.ElementAtOrDefault(1))
        {
            case "buy":
                BuyFromRival(member);
                break;
            case "attack":
                ProvokeRivals(member.Name);
                break;
        }
    }

    /// <summary>Buys what a friendly rival carries, if you can pay and carry it.</summary>
    private void BuyFromRival(RivalMember member)
    {
        if (Rivals is not { IsHostile: false } || member.Dead || RivalItem(member) is not { } item) return;
        if (NoShopping)
            Publish(new MessageEvent(NoShoppingRefusal));
        else if (Player.Gold < member.Price)
            Publish(new MessageEvent($"{member.Name} shakes their head. \"{member.Price} gold, friend. Not a copper less.\""));
        else if (!Player.Inventory.CanCarry(item))
            Publish(new MessageEvent($"You have no room for {Describe(item)}."));
        else
        {
            Player.Gold -= member.Price;
            var price = member.Price;
            member.Carries = null;
            member.Price = 0;
            Knowledge.LearnKind(item.Kind);
            foreach (var rune in item.Runes()) Knowledge.LearnRune(rune);
            var held = Player.Inventory.Add(item)!;
            Publish(new MessageEvent($"You buy {Describe(held)} from {member.Name} for {price} gold."));
            Publish(new ItemBoughtEvent("rival", item.Kind.Id, price));
            RecalculateBonuses();
        }
    }

    /// <summary>You attacked a band at peace with you: hostile for good, every one of them here turns on you.</summary>
    private void ProvokeRivals(string who)
    {
        if (Rivals is not { } band || band.IsHostile) return;
        band.Provoked = true;
        Publish(new MessageEvent($"{who} cries out: \"Treachery!\" {band.Name} won't forget this."));
        AddRivalDeed($"{band.Name} swore vengeance on {(Player.Name.Length > 0 ? Player.Name : "a rival")}", Player.Depth);
        foreach (var m in Level.Monsters.All.Where(m => RivalOf(m) is not null))
        {
            m.Aware = true;
            m.Sleep = 0;
        }
    }

    /// <summary>A rival at peace with you takes harm from you: they are at peace no longer.</summary>
    private void RivalHurt(Monster monster)
    {
        if (RivalAtPeace(monster) && RivalOf(monster) is { } rival) ProvokeRivals(rival.Member.Name);
    }

    /// <summary>
    /// A rival at peace with you takes their turn: they wander a little (not into fire, nor a shop),
    /// and once they have been seen and are out of sight again, now and then go on their way (off the level).
    /// </summary>
    private int RivalTurn(Monster monster)
    {
        if (!monster.IsVisible && monster.EverSeen && Rng.OneIn(10))
        {
            Level.Monsters.Remove(monster);
            return EnergyTable.MoveEnergy;
        }
        if (Rng.OneIn(2)) return EnergyTable.MoveEnergy;
        var steps = Level.Neighbors(monster.Position)
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != Player.Position && !Level.Has(p, TerrainFlags.Fiery)
                        && Level.FeatureAt(p).Shop is null && !Level.Has(p, TerrainFlags.DoorClosed))
            .ToList();
        if (steps.Count > 0) Level.Monsters.Move(monster, steps[Rng.RandInt0(steps.Count)]);
        return EnergyTable.MoveEnergy;
    }

    /// <summary>A rival slain: they drop what they carried (and gold), the news will tell it, and with the last of them the band is gone.</summary>
    private void RivalKilled(Monster monster)
    {
        if (Rivals is not { } band || RivalOf(monster) is not { Member: { Dead: false } member }) return;
        member.Dead = true;
        var killer = Player.Name.Length > 0 ? Player.Name : "a rival";
        member.Fate = $"slain by {killer} at {Feet(Player.Depth)}";
        if (RivalItem(member) is { } item) DropNear(item, monster.Position);
        member.Carries = null;
        member.Price = 0;
        DropNear(MakeLevelGold(Math.Max(1, monster.Race.Depth)), monster.Position);
        if (!band.IsHostile) band.Provoked = true;
        AddRivalDeed($"{member.Title} of {band.Name} fell to {killer} at {Feet(Player.Depth)}", Player.Depth);
        AddHistory($"Slew {member.Title}, of {band.Name}");
        if (band.IsGone)
        {
            AddRivalDeed($"{band.Name} is no more", Player.Depth);
            Publish(new MessageEvent($"{band.Name} is no more."));
        }
    }
}
