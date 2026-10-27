using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// AVABand's cursed levels (LevelCurses; the birth option "Cursed levels"): now and then a new level lies under
// a curse, and you are told its terms as you arrive, before you have moved — stay and take them, or take the
// stairs under you and leave it. The curse's monsters and treasure are made with the level, from dice of the
// level's own, so the game's dice roll as they always did; what it does while you are there (faster monsters,
// a shorter light, richer gold, more experience) holds for as long as the level is cursed, and a level kept
// (persistent levels) keeps its curse. Never in the daily dungeon, the weekly challenge or the tutorial, and
// never in a replay recorded before there were cursed levels.
public sealed partial class GameSession
{
    /// <summary>Whether cursed levels are off however the option stands (a replay recorded before them played without).</summary>
    internal bool BeforeCursedLevels { get; set; }

    /// <summary>Whether this character's new levels may be cursed.</summary>
    public bool CursedLevelsOn => Options[OptionIds.AvaCursedLevels] && !BeforeCursedLevels && !Player.IsSeededRun && !IsTutorial;

    /// <summary>The curse the level you are on lies under, or null.</summary>
    public LevelCurse? LevelCurse => Level is null ? null : LevelCurses.Find(Level.Curse);

    /// <summary>What the status bar shows while you are on a cursed level ("Cursed: Teeming"), or null.</summary>
    public string? CurseStatus => LevelCurse is { } curse ? $"Cursed: {curse.Name}" : null;

    /// <summary>Whether the level you are on lies under this curse (a quick test, for the busy places).</summary>
    private bool CurseIs(LevelCurse curse) => Level?.Curse is { } id && id == curse.Id;

    /// <summary>A new cursed level's curse, to be told once you are on it.</summary>
    private LevelCurse? _curseToTell;

    /// <summary>While you may still leave a cursed level for nothing (until your next command): where you stand.</summary>
    private Loc? _curseChoice;

    // --- Making a level --------------------------------------------------------------------------------

    /// <summary>
    /// Whether a new level at this depth may be cursed: from 250 ft down, not straight after a cursed level,
    /// and never a quest's level (Sauron's and Morgoth's, even once done; one an AVABand quest has its room
    /// or its map on — but the Seal's shrine, waiting on every level until it's found, may be on a cursed one).
    /// </summary>
    private bool MayCurse(int depth) => CursedLevelsOn && depth >= LevelCurses.MinDepth && !Player.LastLevelCursed
        && !Data.Quests.Any(q => q.Level == depth) && (_questRoomHere is null || QuestRoomOptional) && QuestProfileFor(depth) is null;

    /// <summary>For tests: the curse the next new level that may be cursed lies under, whatever its seed says.</summary>
    internal LevelCurse? CurseNextLevel { get; set; }

    /// <summary>A new level, just made: perhaps it is cursed (from its seed). Before it is peopled, so its treasure knows.</summary>
    private void CurseNewLevel()
    {
        var curse = !MayCurse(Level.Depth) ? null : CurseNextLevel ?? LevelCurses.Roll(Level.Seed);
        if (curse is not null) CurseNextLevel = null;
        Level.Curse = curse?.Id;
        _curseToTell = curse;
    }

    /// <summary>
    /// A cursed level, once peopled and furnished: the curse's own monsters and treasure — half as many
    /// monsters and objects again (Teeming), bands of undead (Restless dead), or its rooms dark and something
    /// excellent hidden (Darkness) — made with the level's own dice. Each comes with its friends, asleep and
    /// well away from you.
    /// </summary>
    private void MakeCurse(ISet<string> unavailableUniques)
    {
        if (Level.Curse is null) return;
        var dice = LevelCurses.Dice(Level.Seed);
        var depth = Level.Depth;
        if (CurseIs(LevelCurses.Teeming))
        {
            var placed = Level.SpawnHints.Count(h => h.Kind == SpawnKind.Monster && h.DepthBonus == 0 && h.Tag == "sleep,group");
            for (var i = 0; i < (placed + 1) / 2; i++) Schedule(_spawner.PlaceDistant(Level, dice, Player.Position, 10, depth, unavailableUniques));
            var objects = (Level.Population.RoomObjects + Level.Population.AnywhereObjects + 1) / 2;
            for (var i = 0; i < objects; i++) PlaceCursedObject(dice, Objects.Make(dice, depth, good: true));
        }
        else if (CurseIs(LevelCurses.RestlessDead))
        {
            for (var i = 0; i < 3 + depth / 15; i++)
                Schedule(_spawner.PlaceDistant(Level, dice, Player.Position, 10, depth + 3, unavailableUniques, filter: r => r.Has(MonsterFlags.Undead)));
        }
        else if (CurseIs(LevelCurses.Darkness))
        {
            PutOutTheLights();
            PlaceCursedObject(dice, Objects.Make(dice, depth, good: true, great: true));
        }

        void Schedule(List<Monster>? monsters)
        {
            foreach (var monster in monsters ?? []) Scheduler.Add(monster);
        }
    }

    /// <summary>An object of a curse's, on an empty floor away from you (not in a vault).</summary>
    private void PlaceCursedObject(Randomness.GameRandom dice, Item? item)
    {
        if (item is null) return;
        for (var t = 0; t < 500; t++)
        {
            var p = new Loc(dice.RandRange(1, Level.Width - 2), dice.RandRange(1, Level.Height - 2));
            if (!Level.IsEmptyFloor(p) || Level.Objects.Any(p) || Level[p].Has(SquareFlags.Vault) || p.DistanceTo(Player.Position) <= 5) continue;
            Level.Objects.Add(p, item);
            return;
        }
    }

    /// <summary>Every room's light put out (glowing terrain, like lava, still glows): the weekly challenge's darkness, and this curse's.</summary>
    private void PutOutTheLights()
    {
        Level.IsLit = false;
        foreach (var p in Level.AllLocs())
        {
            ref var sq = ref Level[p];
            if (sq.Has(SquareFlags.Glow) && !Level.FeatureAt(p).Has(TerrainFlags.Bright)) sq.Flags &= ~SquareFlags.Glow;
        }
    }

    // --- While you are there ---------------------------------------------------------------------------

    /// <summary>What the turn scheduler adds to an actor's speed: the weekly challenge's restless foes, a blood moon's.</summary>
    private int SpeedBonus(IActor actor) => TwistSpeedBonus(actor) + CurseSpeedBonus(actor);

    /// <summary>Blood moon: what the turn scheduler adds to a monster's speed (not your hireling's, nor yours).</summary>
    private int CurseSpeedBonus(IActor actor) =>
        Level?.Curse is not null && actor is Monster { IsAlly: false } && CurseIs(LevelCurses.BloodMoon) ? LevelCurses.BloodMoonSpeed : 0;

    /// <summary>Blood moon: how many levels deeper the things found here are made.</summary>
    internal int CurseObjectDepth => Level?.Curse is not null && CurseIs(LevelCurses.BloodMoon) ? LevelCurses.BloodMoonDepth : 0;

    /// <summary>Darkness: your light, a square shorter (a light of one square stays one).</summary>
    private int CurseLight(int light) => light > 1 && CurseIs(LevelCurses.Darkness) ? light - 1 : light;

    /// <summary>Darkness: gold found here, worth twice as much.</summary>
    internal int CurseGold(int value) => CurseIs(LevelCurses.Darkness) ? value * 2 : value;

    /// <summary>Restless dead: an undead slain here is worth twice the experience (its fraction too, in 1/65536ths).</summary>
    internal (int Whole, int Fraction) CurseExperience(MonsterRaceDef race, int whole, int fraction)
    {
        if (!CurseIs(LevelCurses.RestlessDead) || !race.Has(MonsterFlags.Undead)) return (whole, fraction);
        var doubled = fraction * 2;
        return (whole * 2 + doubled / 0x10000, doubled % 0x10000);
    }

    /// <summary>A monster slain on a cursed level (for "Defied the Curse").</summary>
    private void CursedKill()
    {
        if (Level.Curse is not null) Level.CursedKills++;
    }

    /// <summary>A turn (at normal speed) on a cursed level (for "Cursed and Kept").</summary>
    private void CursedTick()
    {
        if (Level.Curse is not null && Player.Depth > 0 && !Player.IsDead) Player.CursedTurns++;
    }

    // --- Arriving and leaving --------------------------------------------------------------------------

    /// <summary>
    /// Leaving a cursed level alive, from 500 ft down, with 30 or more slain on it: the curse defied (and the
    /// count starts again, should the level be kept and come back to).
    /// </summary>
    private void LeavingCursedLevel()
    {
        if (LevelCurse is not { } curse || Player.IsDead) return;
        if (Level.Depth < LevelCurses.DefiedDepth || Level.CursedKills < LevelCurses.DefiedKills) return;
        Level.CursedKills = 0;
        Player.CursesDefied++;
        AddHistory($"Defied the curse ({curse.Name}) at {Level.Depth * Data.Constants.FeetPerLevel} ft");
        Publish(new MessageEvent("You leave the cursed level, its curse defied."));
    }

    /// <summary>
    /// On any level, before you look about you: whether you are on a cursed one now (the next new level
    /// never is, after one), and your light as this level's curse — or the last one's, lifted — leaves it.
    /// </summary>
    private void ArriveCursed(string? leftCurse)
    {
        if (CursedLevelsOn) Player.LastLevelCursed = Level.Curse is not null;
        if (leftCurse == LevelCurses.Darkness.Id || CurseIs(LevelCurses.Darkness)) RecalculateBonuses();
    }

    /// <summary>
    /// A new cursed level, as you arrive: its terms told, and the choice — stay on them, or take the
    /// stairs under you and leave it for nothing. Not on a staircase (a trap door, Word of Recall,
    /// stairs that don't connect), one is made under you if the floor will take it (an up staircase,
    /// a down one when you may only descend); if it won't, you can only read the curse and stay.
    /// </summary>
    private void TellCurse()
    {
        if (_curseToTell is not { } curse) return;
        _curseToTell = null;
        Publish(new MessageEvent($"A curse lies on this level: {curse.Name}."));
        var down = StairsToLeave();
        Publish(new LevelCursedEvent(curse.Id, curse.Name, curse.Risk, curse.Reward, down is not null));
        var text = $"{curse.Risk}\nIn return: {curse.Reward}\n\n"
                   + (down is null
                       ? "There is no way back from here: you must take the curse's terms."
                       : "Stay and take its terms, or take the stairs and leave the level as it is — that costs you nothing but the level.");
        var choices = new List<(string Id, string Label)> { ("cursed:stay", "Stay, on these terms") };
        if (down is { } d)
        {
            choices.Add(("cursed:leave", $"Take the stairs {(d ? "down" : "up")} and leave it"));
            _curseChoice = Player.Position;
        }
        AskQuest($"A cursed level: {curse.Name}", text, [.. choices]);
    }

    /// <summary>The staircase you could leave by — true for down, false for up — making one under you if need be; null if none.</summary>
    private bool? StairsToLeave()
    {
        var at = Player.Position;
        if (Level.Has(at, TerrainFlags.DownStair)) return true;
        if (Level.Has(at, TerrainFlags.UpStair) && !ForceDescend) return false;
        if (!Level.IsEmptyFloor(at) || Level.Objects.Any(at) || Level[at].Has(SquareFlags.Vault)) return null;
        Level[at].Feature = ForceDescend ? Data.Terrain.Ids.DownStair : Data.Terrain.Ids.UpStair;
        Known.Remember(Level, at);
        return ForceDescend;
    }

    /// <summary>Any command but an answer: the chance to leave a cursed level for nothing has gone.</summary>
    private void CurseChoiceGone(GameCommand command)
    {
        if (command is not QuestChoiceCommand) _curseChoice = null;
    }

    /// <summary>The answer to a cursed level's question: stay, or (if you haven't moved since) take the stairs and leave it.</summary>
    private int CursedChoice(string[] parts)
    {
        var open = _curseChoice == Player.Position && LevelCurse is not null;
        _curseChoice = null;
        if (parts.ElementAtOrDefault(1) != "leave" || !open)
        {
            if (LevelCurse is { } curse) Publish(new MessageEvent($"You stay, and take the terms of the curse ({curse.Name})."));
            return 0;
        }
        Publish(new MessageEvent("You turn from the curse, and take the stairs."));
        return TakeStairs(Level.Has(Player.Position, TerrainFlags.DownStair));
    }
}
