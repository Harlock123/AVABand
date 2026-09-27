using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.World;

namespace Angband.Core.Game;

// Quests (Angband quest.txt / player-quest.c): Sauron waits on level 99 and Morgoth on level 100.
// A quest level has no way down until its guardian is dead, nothing carries the player past an
// unfinished quest, and killing the last guardian wins the game.
public sealed partial class GameSession
{
    /// <summary>Kills toward each quest, by quest id.</summary>
    public Dictionary<string, int> QuestKills { get; } = new(StringComparer.Ordinal);

    public bool IsQuestComplete(QuestDef quest) => QuestKills.GetValueOrDefault(quest.Id) >= quest.Number;

    /// <summary>The unfinished quest on a level, if any.</summary>
    public QuestDef? QuestAt(int depth) => Data.Quests.FirstOrDefault(q => q.Level == depth && !IsQuestComplete(q));

    /// <summary>
    /// Angband dungeon_get_next_level: moving from <paramref name="from"/> towards <paramref name="to"/>
    /// stops at the first unfinished quest level on the way down.
    /// </summary>
    public int CapDepth(int from, int to)
    {
        if (to <= from) return to;
        var quest = Data.Quests.FirstOrDefault(q => q.Level > from && q.Level < to && !IsQuestComplete(q));
        return quest?.Level ?? to;
    }

    /// <summary>
    /// A new level that holds a quest: no down staircases or trap doors, and the quest's monsters
    /// (those still alive) waiting somewhere away from the player.
    /// </summary>
    private void PrepareQuestLevel()
    {
        if (QuestAt(Level.Depth) is not { } quest) return;
        foreach (var p in Level.AllLocs())
        {
            ref var sq = ref Level[p];
            if (Level.FeatureAt(p).Has(TerrainFlags.DownStair)) sq.Feature = Data.Terrain.Ids.Floor;
            if (sq.Trap != 0 && Data.TrapByIndex(sq.Trap)?.IsTrapDoor == true) sq.Trap = 0;
        }
        if (Data.Monster(quest.Race) is not { } race) return;
        var present = Level.Monsters.All.Count(m => m.Race.Id == race.Id);
        var needed = quest.Number - QuestKills.GetValueOrDefault(quest.Id) - present;
        var spots = Level.AllLocs()
            .Where(p => Level.IsEmptyFloor(p) && p.DistanceTo(Player.Position) > 15)
            .OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (spots.Count == 0)
            spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p != Player.Position).ToList();
        for (var i = 0; i < needed && spots.Count > 0; i++)
        {
            var at = Rng.Pick(spots);
            spots.Remove(at);
            var m = _spawner.Place(Level, Rng, race, at);
            Scheduler.Add(m);
        }
    }

    /// <summary>Counts a kill toward its quest; finishing a quest opens the way down, the last one wins.</summary>
    private void QuestKill(Monster monster)
    {
        var race = monster.OriginalRace ?? monster.Race;
        var quest = Data.Quests.FirstOrDefault(q => q.Race == race.Id && !IsQuestComplete(q));
        if (quest is null) return;
        QuestKills[quest.Id] = QuestKills.GetValueOrDefault(quest.Id) + 1;
        if (!IsQuestComplete(quest)) return;

        Publish(new QuestCompletedEvent(quest.Id));
        if (Data.Quests.All(IsQuestComplete))
        {
            Player.IsWinner = true;
            Publish(new MessageEvent("*** CONGRATULATIONS ***"));
            Publish(new MessageEvent("You have won the game!"));
            Publish(new MessageEvent("You may retire (Game menu, or Q) when you are ready."));
            Publish(new GameWonEvent());
        }
        else if (Level.Depth == quest.Level && Level.Depth < Data.Constants.MaxDepth)
        {
            // Angband build_quest_stairs: a staircase down appears where the guardian fell.
            var at = StairSpotNear(monster.Position);
            Level[at].Feature = Data.Terrain.Ids.DownStair;
            Known.Remember(Level, at);
            Publish(new MessageEvent("A magical staircase appears..."));
            UpdateView();
        }
    }

    private Loc StairSpotNear(Loc p)
    {
        for (var r = 0; r <= 5; r++)
            foreach (var q in Level.AllLocs().Where(q => q.ChebyshevTo(p) == r).OrderBy(q => q.Y).ThenBy(q => q.X))
                if (Level.IsFloor(q) && Level.Objects.At(q).Count == 0 && q != Player.Position) return q;
        return p;
    }

    /// <summary>A winner retires (Angband: after Morgoth falls), ending the game with their victory recorded.</summary>
    private int Retire()
    {
        if (!Player.IsWinner)
        {
            Publish(new MessageEvent("You can retire once you have defeated Morgoth."));
            return 0;
        }
        Player.IsDead = true;
        Player.KilledBy = "Ripe Old Age";
        Publish(new MessageEvent("You retire from adventuring, victorious."));
        Publish(new PlayerDiedEvent(Player.KilledBy, Player.Depth));
        return 0;
    }
}
