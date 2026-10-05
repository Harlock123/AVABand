using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// More of the notice board's jobs (AVABand's own): a parcel to carry to a shop in town (it rides in your
// quest satchel; walking into the shop delivers it), and a traveller to rescue at a depth before time runs
// out (arriving there, you hear them; step to them and they're free — too late, and the note is crossed
// out). Paid at the inn, as the others are.
public sealed partial class GameSession
{
    /// <summary>How long a rescue gives you, in game turns (about three thousand of your turns at normal speed).</summary>
    public const long RescueTime = 30_000;

    /// <summary>The shops a parcel may go to.</summary>
    private static readonly string[] ParcelShops = ["general", "armoury", "weaponsmith", "bookseller", "alchemist", "magic"];

    private string ShopName(string shopId) => Data.Shops.FirstOrDefault(s => s.Id == shopId)?.Name ?? shopId;

    private BoardJob? MakeParcel(int deepest)
    {
        var shop = ParcelShops[Rng.RandInt0(ParcelShops.Length)];
        if (AvaQuests.Board.Any(j => j.Kind == "parcel")) return null;
        return new BoardJob { Id = AvaQuests.NextJobId++, Kind = "parcel", Target = shop, Count = 1, Reward = 40 + 5L * deepest };
    }

    private BoardJob? MakeRescue(int deepest)
    {
        var depth = Math.Min(Data.Constants.MaxDepth - 1, Math.Max(1, deepest + Rng.RandInt0(3) - 1));
        if (AvaQuests.Board.Any(j => j.Kind == "rescue")) return null;
        return new BoardJob
        {
            Id = AvaQuests.NextJobId++, Kind = "rescue", Target = depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Count = depth, Reward = 150 + 30L * depth,
        };
    }

    /// <summary>A job taken: a parcel into the satchel; a rescue's clock starts.</summary>
    private void TakeErrand(BoardJob job)
    {
        if (job.Kind == "parcel") GiveQuestItem(QuestItem("sealed_parcel", $"board:{job.Id}"));
        if (job.Kind == "rescue") job.Deadline = GameTurn + RescueTime;
    }

    /// <summary>A job given up: its parcel back to Butterbur.</summary>
    private void DropErrand(BoardJob job)
    {
        foreach (var parcel in Player.Inventory.Pack.Where(i => i.QuestTag == $"board:{job.Id}").ToList())
            Player.Inventory.Remove(parcel, parcel.Number, () => Objects.NextSerial++);
    }

    /// <summary>Walking into a shop: a parcel for it is handed over.</summary>
    private void DeliverParcels(string shopId)
    {
        foreach (var job in AvaQuests.Board.Where(j => j.Taken && j.Kind == "parcel" && j.Target == shopId && j.Progress == 0).ToList())
        {
            if (Player.Inventory.Pack.FirstOrDefault(i => i.QuestTag == $"board:{job.Id}") is not { } parcel) continue;
            Player.Inventory.Remove(parcel, parcel.Number, () => Objects.NextSerial++);
            job.Progress = 1;
            Publish(new MessageEvent($"You hand the parcel over at the {ShopName(shopId)}. \"From Butterbur? Tell him it came safe.\""));
        }
    }

    /// <summary>"about 1,200 turns left", or "too late".</summary>
    private string RescueTimeLeft(BoardJob job) =>
        GameTurn > job.Deadline ? "too late" : $"about {(job.Deadline - GameTurn) / 10:N0} turns left";

    /// <summary>A rescue past its deadline is crossed off (checked on each new level and each return to town).</summary>
    private void CheckRescueDeadlines()
    {
        foreach (var job in AvaQuests.Board.Where(j => j.Taken && j.Kind == "rescue" && j.Progress == 0 && GameTurn > j.Deadline).ToList())
        {
            AvaQuests.Board.Remove(job);
            Publish(new MessageEvent($"Too late: the traveller trapped at {job.Count * Data.Constants.FeetPerLevel} ft. The note is crossed out."));
        }
    }

    /// <summary>A new level: a rescue at this depth puts its traveller somewhere on it, far from you.</summary>
    private void PlaceRescues()
    {
        CheckRescueDeadlines();
        foreach (var job in AvaQuests.Board.Where(j => j.Taken && j.Kind == "rescue" && j.Progress == 0 && j.Count == Level.Depth))
        {
            var from = Player.Position;
            if (Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p.DistanceTo(from) >= 12 && FindPathAnywhere(from, p))
                    .OrderByDescending(p => p.DistanceTo(from)).ThenBy(p => p.Y).ThenBy(p => p.X).Select(p => (Loc?)p).FirstOrDefault() is not { } spot)
                continue;
            Level[spot].Feature = Data.Terrain["trapped_apprentice"].Index;
            (job.AtX, job.AtY, job.LevelSeed) = (spot.X, spot.Y, Level.Seed);
            Publish(new MessageEvent("Somewhere on this level, someone is crying for help."));
        }
    }

    /// <summary>Whether a square can be walked to at all (passable squares, any of them, known or not).</summary>
    private bool FindPathAnywhere(Loc from, Loc to)
    {
        var seen = new HashSet<Loc> { from };
        var queue = new Queue<Loc>([from]);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == to) return true;
            foreach (var n in Level.Neighbors(p))
                if ((Level.IsPassable(n) || Level.IsDoor(n)) && seen.Add(n)) queue.Enqueue(n);
        }
        return false;
    }

    /// <summary>Stepping onto a trapped traveller: they're free.</summary>
    private bool RescueStep(Loc at)
    {
        if (AvaQuests.Board.FirstOrDefault(j => j.Taken && j.Kind == "rescue" && j.Progress == 0 && j.AtX == at.X && j.AtY == at.Y
                                                && j.LevelSeed == Level.Seed) is not { } job)
            return false;
        job.Progress = 1;
        Level[at].Feature = Data.Terrain.Ids.Floor;
        Known.Remember(Level, at);
        Publish(new MessageEvent("You free the trapped traveller. \"Bless you! I'll find my own way up — tell Butterbur I'm coming.\""));
        Disturb();
        return true;
    }
}
