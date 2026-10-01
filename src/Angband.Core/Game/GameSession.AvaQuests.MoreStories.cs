using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// Three more of AVABand's story quests (their words are in ava_quests.json), each hooked in from
// GameSession.AvaQuests.Stories.cs:
//  - The Apprentice: the Alchemist's apprentice, trapped behind a rockfall and guarded. Give him your
//    own Scroll of Word of Recall and he is home at once; or show him the way and let him go alone,
//    and learn at the Alchemy shop whether he made it.
//  - The Cartographer: map three quarters of a cavern, a labyrinth and one of the old mines (magic
//    mapping counts), and take the maps to the Bookseller.
//  - The Warden's Fires: the Shade of the Stair can't be harmed while its hall's three braziers are
//    cold. Light them with the Warden's Taper — and keep it from snuffing them out again.
public sealed partial class GameSession
{
    private static readonly string[] MapKinds = ["cavern", "labyrinth", "moria"];

    private static string MapName(string profile) => profile switch
    {
        "cavern" => "a cavern",
        "labyrinth" => "a labyrinth",
        _ => "the old mines",
    };

    /// <summary>The depths and counts these quests settle when they're offered.</summary>
    private bool PlanMoreQuest(AvaQuestState s)
    {
        if (PlanDeepQuest(s)) return true;
        switch (s.Id)
        {
            case "apprentice":
                s.Numbers["depth"] = QuestDepth(2, 10, 30);
                s.Stage = "find";
                return true;
            case "cartographer":
                // Where he's heard each kind is to be found, a few levels on from your deepest.
                s.Numbers["cavern_depth"] = QuestDepth(2, 5, 30);
                s.Numbers["labyrinth_depth"] = s.N("cavern_depth") + 2 + Rng.RandInt0(3);
                s.Numbers["moria_depth"] = s.N("labyrinth_depth") + 2 + Rng.RandInt0(3);
                foreach (var kind in MapKinds) s.Numbers[kind] = 0;
                s.Numbers["mapped"] = 0;
                s.Texts["which"] = "none yet";
                s.Stage = "map";
                return true;
            case "warden":
                s.Numbers["depth"] = QuestDepth(2, 20, 36);
                s.Numbers["lit"] = 0;
                s.Stage = "light";
                return true;
        }
        return false;
    }

    private void MoreQuestAccepted(AvaQuestState s)
    {
        if (s.Id == "warden") GiveQuestItem(QuestItem("wardens_taper", "warden"));
    }

    private (string Quest, string Room)? MoreRoomFor(int depth)
    {
        if (DeepRoomFor(depth) is { } deep) return deep;
        if (ChiselRoomFor(depth) is { } chisel) return chisel;
        if (Active("apprentice") is { Stage: "find" } apprentice && depth == apprentice.N("depth")) return ("apprentice", "quest_apprentice_cave");
        if (Active("warden") is { } warden && depth == warden.N("depth") && !KilledUniques.Contains("the_shade_of_the_stair"))
            return ("warden", "quest_warden_hall");
        return null;
    }

    /// <summary>
    /// The kind of level a quest wants made at this depth (null: as the dungeon chooses): the
    /// cartographer's cavern, labyrinth and old mines, each at the depth he named, until mapped.
    /// </summary>
    private string? QuestProfileFor(int depth)
    {
        if (!AvaQuestsOn || depth <= 0 || Active("cartographer") is not { Stage: "map" } maps) return null;
        return MapKinds.FirstOrDefault(kind => maps.N(kind) == 0 && maps.N(kind + "_depth") == depth);
    }

    private IEnumerable<Loc> QuestSpots(char symbol) => Level.QuestSpots.Where(s => s.Symbol == symbol).Select(s => s.Loc);

    private bool FurnishMoreRoom(string quest)
    {
        if (FurnishDeepRoom(quest)) return true;
        if (FurnishChiselRoom(quest)) return true;
        var depth = Level.Depth;
        switch (quest)
        {
            case "apprentice":
                if (QuestSpot('(') is { } hollow) SetQuestFeature(hollow, "trapped_apprentice");
                // Something found him before you did.
                if (QuestSpot(')') is { } guard && _spawner.PickRace(Rng, depth + 3, new HashSet<string>(KilledUniques),
                        r => !r.IsUnique && !r.Has(MonsterFlags.NeverMove), allowOutOfDepth: false) is { } race)
                    PlaceQuestMonster(race.Id, guard, asleep: true);
                Publish(new MessageEvent("Somewhere on this level, someone is calling for help."));
                return true;
            case "warden" when Active("warden") is { } warden:
                // A new level: the fires as they stand — all burning once the Shade can die, cold before.
                var lit = warden.Stage == "slay";
                foreach (var spot in QuestSpots('(')) SetQuestFeature(spot, lit ? "lit_brazier" : "cold_brazier");
                if (!lit) warden.Numbers["lit"] = 0;
                if (QuestSpot(')') is { } shade) PlaceQuestMonster("the_shade_of_the_stair", shade);
                Publish(new MessageEvent(lit ? "The wardens' fires burn here still, and something cold waits among them."
                    : "The air is cold and dead: the Wardens' hall is on this level, its fires out."));
                return true;
        }
        return false;
    }

    private bool MoreStep(Loc at, string feature)
    {
        switch (feature)
        {
            case "trapped_apprentice":
                if (Active("apprentice") is not { Stage: "find" })
                {
                    Publish(new MessageEvent("There's no one here now."));
                    return true;
                }
                var choices = new List<(string, string)>();
                if (Carrying("scroll_of_word_of_recall") is not null) choices.Add(("apprentice:recall", "Give him your Scroll of Word of Recall"));
                choices.Add(("apprentice:alone", "Show him the way up, and let him go alone"));
                choices.Add(("none", "Not yet"));
                AskQuest("The trapped apprentice", "He looks up at you, grey with dust, and nearly cries. \"The Alchemist sent you? "
                                                  + "I can't get out alone. The way up is crawling with them.\"", [.. choices]);
                return true;
            case "cold_brazier":
                if (Active("warden") is not { Stage: "light" } warden) return true;
                if (Carrying("wardens_taper") is null)
                {
                    Publish(new MessageEvent("The brazier is cold. You have nothing to light it with that would hold against the Shade."));
                    return true;
                }
                SetQuestFeature(at, "lit_brazier");
                UpdateView();
                warden.Numbers["lit"] = warden.N("lit") + 1;
                if (warden.N("lit") >= 3)
                {
                    Publish(new MessageEvent("The third fire catches, and all three roar up together. Somewhere a thin shriek rises and breaks: the Shade can be harmed now."));
                    SetStage(warden, "slay");
                }
                else Publish(new MessageEvent($"You touch the taper to the ash and the brazier roars into flame. ({warden.N("lit")} of 3 lit.)"));
                return true;
            case "lit_brazier":
                Publish(new MessageEvent("The fire burns high and bright."));
                return true;
        }
        return false;
    }

    private bool MoreAtShop(string shopId)
    {
        if (DeepAtShop(shopId)) return true;
        switch (shopId)
        {
            case "alchemist" when Active("apprentice") is { Stage: "rescued" }:
                AskQuest("The Alchemy shop", "The apprentice is behind the counter, scrubbed and bandaged, sorting cave-moss. The Alchemist comes round to shake your hand.",
                    ("apprentice:reward", "Accept his thanks"));
                return true;
            case "alchemist" when Active("apprentice") is { Stage: "alone" } alone:
                if (alone.N("madeit") == 1)
                    AskQuest("The Alchemy shop", "The apprentice is back, limping, one arm in a sling. \"I made it,\" he says. \"Only just.\"",
                        ("apprentice:limped", "Accept their thanks"));
                else
                    AskQuest("The Alchemy shop", "The Alchemist looks up as you come in, and you can see he's been waiting. \"He didn't come home.\"",
                        ("apprentice:lost", "Leave him to it"));
                return true;
            case "bookseller" when Active("cartographer") is { Stage: "deliver" }:
                AskQuest("The Bookseller", "The cartographer is here, waiting, and unrolls your maps across the counter with inky hands.",
                    ("cartographer:deliver", "Hand over the maps"), ("store:bookseller", "Just shop"));
                return true;
        }
        return false;
    }

    private bool MoreChoice(string[] parts)
    {
        if (DeepChoice(parts)) return true;
        switch (parts[0])
        {
            case "apprentice" when parts[1] == "recall" && Active("apprentice") is { Stage: "find" } a && Carrying("scroll_of_word_of_recall") is { } scroll:
                Player.Inventory.Remove(scroll, 1, () => Objects.NextSerial++);
                ClearApprentice();
                Publish(new MessageEvent("He reads your scroll with shaking hands; the air folds round him, and he is gone. You'll need another way home."));
                GainExperience(40L * Level.Depth);
                SetStage(a, "rescued");
                return true;
            case "apprentice" when parts[1] == "alone" && Active("apprentice") is { Stage: "find" } b:
                ClearApprentice();
                // Whether he makes it is settled now (the deeper, the worse his odds), not when you ask.
                b.Numbers["madeit"] = Rng.RandInt0(100) < Math.Clamp(80 - 2 * b.N("depth"), 20, 70) ? 1 : 0;
                Publish(new MessageEvent("You point out the way you came, and what to keep clear of. He goes, not looking back."));
                SetStage(b, "alone");
                return true;
            case "apprentice" when parts[1] == "reward" && Active("apprentice") is { Stage: "rescued" } c:
                Player.Gold += 30L * Player.Level;
                for (var i = 0; i < 3; i++) GiveQuestItem(Objects.Create("potion_of_cure_critical_wounds"));
                Adjust("alchemist", -15);
                Publish(new MessageEvent("\"Anything in the shop, a sixth off, for as long as I keep it.\""));
                SetStage(c, "done");
                OpenStoreAfterAsking("alchemist");
                return true;
            case "apprentice" when parts[1] == "limped" && Active("apprentice") is { Stage: "alone" } d:
                GiveQuestItem(Objects.Create("potion_of_cure_critical_wounds"));
                Adjust("alchemist", -5);
                SetStage(d, "done");
                OpenStoreAfterAsking("alchemist");
                return true;
            case "apprentice" when parts[1] == "lost" && Active("apprentice") is { Stage: "alone" } e:
                e.Stage = "lost";
                Publish(new MessageEvent("Quest over: The Apprentice."));
                OpenStoreAfterAsking("alchemist");
                return true;
            case "cartographer" when parts[1] == "deliver" && Active("cartographer") is { Stage: "deliver" } f:
                var pay = 150 + 25L * Player.Level;
                Player.Gold += pay;
                GiveQuestItem(Objects.Create("rod_of_treasure_location"));
                Adjust("bookseller", -15);
                Publish(new MessageEvent($"The Bookseller counts out {pay} gold, and the cartographer presses a rod into your hand: \"For finding the rest.\""));
                SetStage(f, "done");
                OpenStoreAfterAsking("bookseller");
                return true;
        }
        return false;
    }

    /// <summary>The apprentice gone from his hollow (home, or on his way).</summary>
    private void ClearApprentice()
    {
        if (_questPlace is { } at && Level.FeatureAt(at).Id == "trapped_apprentice") SetQuestFeature(at, "floor");
    }

    private bool MoreUse(string kindId)
    {
        if (DeepUse(kindId)) return true;
        if (ChiselUse(kindId)) return true;
        if (kindId != "wardens_taper") return false;
        Publish(new MessageEvent("Step onto each cold brazier in the Wardens' hall to light it."));
        return true;
    }

    private void MoreMonsterKilled(Monster monster)
    {
        DeepMonsterKilled(monster);
        if (monster.Race.Id == "the_shade_of_the_stair" && Active("warden") is { Stage: "slay" } warden)
        {
            GainExperience(150L * Level.Depth);
            SetStage(warden, "done");
        }
    }

    private bool MoreUndying(Monster monster)
    {
        if (monster.Race.Id != "the_shade_of_the_stair" || Active("warden") is not { Stage: "light" }) return false;
        monster.Hp = monster.MaxHp;
        if (monster.IsVisible) Publish(new MessageEvent("The Shade of the Stair parts like smoke round the blow, and gathers itself again."));
        return true;
    }

    private void MoreUpkeep()
    {
        DeepUpkeep();
        // The Shade puts out a fire it lingers by (while it still can): about one turn in six at normal speed.
        if (GameTurn % 10 == 0 && Active("warden") is { Stage: "light" } warden && warden.N("lit") > 0
            && Level.Monsters.All.FirstOrDefault(m => m.Race.Id == "the_shade_of_the_stair") is { } shade
            && NextToLitBrazier(shade.Position) && Rng.OneIn(6))
            foreach (var d in DirectionExtensions.Compass)
            {
                var at = shade.Position.Step(d);
                if (!Level.InBounds(at) || Level.FeatureAt(at).Id != "lit_brazier") continue;
                SetQuestFeature(at, "cold_brazier");
                UpdateView();
                warden.Numbers["lit"] = warden.N("lit") - 1;
                Publish(new MessageEvent(shade.IsVisible ? "The Shade of the Stair passes over a brazier, and the fire gutters out!"
                    : "Somewhere in the hall, a fire gutters out."));
                break;
            }

        // The cartographer's maps: checked now and then, on the kinds of level he wants.
        if (GameTurn % 50 == 0 && Active("cartographer") is { Stage: "map" } maps && Level.Depth > 0
            && Array.IndexOf(MapKinds, Level.ProfileId) is >= 0 && maps.N(Level.ProfileId) == 0 && MappedFraction() >= 0.75)
        {
            maps.Numbers[Level.ProfileId] = 1;
            maps.Numbers["mapped"] = MapKinds.Count(k => maps.N(k) != 0);
            maps.Texts["which"] = string.Join(", ", MapKinds.Where(k => maps.N(k) != 0).Select(MapName));
            Publish(new MessageEvent($"You have mapped enough of {MapName(Level.ProfileId)} for the cartographer."));
            SetStage(maps, maps.N("mapped") >= 3 ? "deliver" : "map");
        }
    }

    private bool NextToLitBrazier(Loc at) =>
        DirectionExtensions.Compass.Any(d => at.Step(d) is var n && Level.InBounds(n) && Level.FeatureAt(n).Id == "lit_brazier");

    /// <summary>How much of the level's open ground you know (seen, remembered or magically mapped).</summary>
    public double MappedFraction()
    {
        int open = 0, known = 0;
        foreach (var p in Level.AllLocs())
        {
            if (!Level.IsPassable(p)) continue;
            open++;
            if (Known.IsKnown(p)) known++;
        }
        return open == 0 ? 0 : (double)known / open;
    }
}
