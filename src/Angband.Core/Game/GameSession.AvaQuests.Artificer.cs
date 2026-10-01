using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// The Arcane Artificer's door ('0'), and SlatriBartSlow's own quest, the Artificer's Chisel
// (GameSession.Artificer.cs has his sockets). Walking in: he offers to cut a socket into anything of
// yours that could take one, each at its price; from level 20 he speaks of the chisel he lost in a
// workshop below; bring it back and he cuts one socket free, never slips again and asks half — or keep
// it, and cut one socket yourself (using the chisel), even into an artifact.
public sealed partial class GameSession
{
    /// <summary>The level from which SlatriBartSlow speaks of his lost chisel.</summary>
    public const int ChiselQuestLevel = 20;

    /// <summary>Walking into the Arcane Artificer.</summary>
    private void EnterArtificer()
    {
        var chisel = AvaQuests.Get("chisel");
        // The chisel brought back.
        if (chisel is { Stage: "found" } && Carrying("star_forged_chisel") is not null)
        {
            AskQuest("The Arcane Artificer", $"{ArtificerName} sees what you carry, and goes very still. \"My chisel. You found it. "
                     + "Will you give it back? I'd cut you a socket for nothing, and never slip on your gear again — and ask half, after.\"",
                ("chisel:return", "Give it back"), ("chisel:keep", "Keep it (and cut one socket yourself, any day)"), ("artificer:menu", "Not yet"));
            return;
        }
        ArtificerMenu();
    }

    /// <summary>The artificer's work: a socket for anything that could take one, and (from level 20) his trouble.</summary>
    private void ArtificerMenu()
    {
        var choices = new List<(string, string)>();
        var free = AvaQuests.Get("chisel") is { Stage: "returned" } r && r.N("freecut") > 0;
        foreach (var piece in SocketablePieces().Take(12))
            choices.Add(($"artificer:cut:{piece.Serial}",
                $"A socket in your {ItemNaming.Describe(piece, Knowledge, withArticle: false, full: false)}"
                + $"{(Player.Inventory.Equipped.Contains(piece) ? " (worn)" : "")} — {(free ? "free" : $"{SocketCost(piece)} gold")}"));
        if (AvaQuests.Get("chisel") is null && Player.Level >= ChiselQuestLevel && Data.AvaQuest("chisel") is not null)
            choices.Add(("artificer:trouble", "\"You look troubled.\""));
        choices.Add(("none", "Leave"));
        var greeting = choices.Count == 1
            ? $"{ArtificerName} looks up from his bench. \"Nothing of yours I can open up today, friend. Bring me armour, a shield, a weapon...\""
            : $"{ArtificerName} looks up from his bench, a loupe in one eye. \"A socket, is it? I can cut one into nearly anything you wear. "
              + (free ? "The first is on me, for the chisel." : AvaQuests.Get("chisel") is { Stage: "returned" }
                  ? "Half price, for you — and my hand won't slip."
                  : "It isn't cheap, and now and then the stone fights back — I'll not lie to you.")
              + $" You have {Player.Gold} gold.\"";
        AskQuest("The Arcane Artificer", greeting, [.. choices]);
    }

    /// <summary>The artificer's and the chisel's choices.</summary>
    private void ArtificerChoice(string[] parts)
    {
        switch (parts[0], parts.ElementAtOrDefault(1))
        {
            case ("artificer", "menu"):
                ArtificerMenu();
                return;
            case ("artificer", "trouble"):
                if (Data.AvaQuest("chisel") is { } def && AvaQuests.Get("chisel") is null)
                {
                    var state = PlanQuest("chisel");
                    _plannedQuest = state;
                    AskQuest(def.Name, Fill(def.Offer, state), ("accept:chisel", "Take it on"), ("artificer:menu", "Not now"));
                }
                return;
            case ("artificer", "cut") when long.TryParse(parts.ElementAtOrDefault(2), out var serial)
                                          && SocketablePieces().FirstOrDefault(i => i.Serial == serial) is { } piece:
            {
                var chisel = AvaQuests.Get("chisel");
                var free = chisel is { Stage: "returned" } && chisel.N("freecut") > 0;
                if (ArtificerCuts(piece, free) && free) chisel!.Numbers["freecut"] = 0;
                ArtificerMenu(); // another? (or leave)
                return;
            }
            case ("chisel", "return") when AvaQuests.Get("chisel") is { Stage: "found" } q && Carrying("star_forged_chisel") is { } chisel:
                Player.Inventory.Remove(chisel, chisel.Number, () => Objects.NextSerial++);
                q.Numbers["returned"] = 1;
                q.Numbers["freecut"] = 1;
                GainExperience(60L * Math.Max(1, q.N("depth")));
                Publish(new MessageEvent($"{ArtificerName} turns the chisel in the light, and laughs. \"Home. Now — what shall I open up for you?\""));
                SetStage(q, "returned");
                ArtificerMenu();
                return;
            case ("chisel", "keep") when AvaQuests.Get("chisel") is { Stage: "found" } q:
                Publish(new MessageEvent($"{ArtificerName}'s face closes. \"Keep it, then. One cut is all it has left in it for anyone but me.\""));
                SetStage(q, "kept");
                return;
            case ("chisel", "cut") when long.TryParse(parts.ElementAtOrDefault(2), out var serial)
                                       && Player.Inventory.All.FirstOrDefault(i => i.Serial == serial) is { } piece:
                if (AvaQuests.Get("chisel") is { Stage: "found" } found) SetStage(found, "kept");
                CutSocketYourself(piece);
                return;
        }
    }

    /// <summary>Using the chisel: one socket, cut by your own hand (keeping it, if you hadn't decided).</summary>
    private bool ChiselUse(string kindId)
    {
        if (kindId != "star_forged_chisel") return false;
        var pieces = Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Where(i => CanTakeSocket(i, byChisel: true)).Take(12).ToList();
        if (pieces.Count == 0)
        {
            Publish(new MessageEvent("Nothing you have would take a socket."));
            return true;
        }
        var choices = pieces.Select(p => ($"chisel:cut:{p.Serial}",
            $"Your {ItemNaming.Describe(p, Knowledge, withArticle: false, full: false)}{(Player.Inventory.Equipped.Contains(p) ? " (worn)" : "")}")).ToList();
        choices.Add(("none", "Not now"));
        AskQuest("The star-forged chisel", AvaQuests.Get("chisel") is { Stage: "found" }
                ? "It would cut once more for you — and then it's yours, not his. Into what?"
                : "It would cut once more for you. Into what?", [.. choices]);
        return true;
    }

    // --- The quest's level: a fallen workshop with the chisel in it ----------------------------------

    private bool PlanChiselQuest(AvaQuestState s)
    {
        if (s.Id != "chisel") return false;
        s.Numbers["depth"] = QuestDepth(3, 20, 40);
        s.Stage = "find";
        return true;
    }

    private (string Quest, string Room)? ChiselRoomFor(int depth) =>
        Active("chisel") is { Stage: "find" } c && depth == c.N("depth") ? ("chisel", "quest_artificer_workshop") : null;

    private bool FurnishChiselRoom(string quest)
    {
        if (quest != "chisel") return false;
        if (QuestSpot('(') is { } bench) Level.Objects.Add(bench, QuestItem("star_forged_chisel", "chisel"));
        foreach (var guard in QuestSpots(')'))
            if (_spawner.PickRace(Rng, Level.Depth + 4, new HashSet<string>(KilledUniques), r => !r.IsUnique && !r.Has(MonsterFlags.NeverMove),
                    allowOutOfDepth: false) is { } race)
                PlaceQuestMonster(race.Id, guard, asleep: true);
        Publish(new MessageEvent("You hear the ring of a hammer on stone, far off — then nothing."));
        return true;
    }

    private void ChiselPickedUp(Item item)
    {
        if (item.Kind.Id == "star_forged_chisel" && Active("chisel") is { Stage: "find" } c) SetStage(c, "found");
    }
}
