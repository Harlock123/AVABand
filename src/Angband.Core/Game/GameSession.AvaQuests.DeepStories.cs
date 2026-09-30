using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// AVABand's deep quests, for the long middle of the game (their words are in ava_quests.json),
// hooked in from GameSession.AvaQuests.MoreStories.cs:
//  - The Heart of the Mountain: Skorvath the Cold-drake lies on the dwarves' hoard; take back the Heart and
//    give it to the dwarf at the inn — or keep it: worn, it lends strength, and calls dragons.
//  - The Last Watch: hold a watchtower with the rangers against an orc-host, 200 turns, war-bands
//    coming every thirty — or kill Grishnag, their warchief.
//  - The Seeing Stone: a palantír in a drowned vault, kept by what drowned with it. Looked into, it
//    shows the whole level and all on it, but the Eye may look back; give it to the White Council's
//    messenger at the Bookseller, or keep it.
public sealed partial class GameSession
{
    /// <summary>How long the rangers need their watchtower held, in turns on its level.</summary>
    public const int WatchTurns = 200;

    private bool PlanDeepQuest(AvaQuestState s)
    {
        switch (s.Id)
        {
            case "heart":
                s.Numbers["depth"] = QuestDepth(3, 38, 50);
                s.Stage = "hunt";
                return true;
            case "watch":
                s.Numbers["depth"] = QuestDepth(3, 52, 64);
                s.Numbers["held"] = 0;
                s.Stage = "hold";
                return true;
            case "stone":
                s.Numbers["depth"] = QuestDepth(3, 50, 62);
                s.Stage = "seek";
                return true;
        }
        return false;
    }

    private (string Quest, string Room)? DeepRoomFor(int depth)
    {
        if (Active("heart") is { Stage: "hunt" } heart && depth == heart.N("depth")) return ("heart", "quest_dragon_hoard");
        if (Active("watch") is { Stage: "hold" } watch && depth == watch.N("depth")) return ("watch", "quest_watchtower");
        if (Active("stone") is { Stage: "seek" } stone && depth == stone.N("depth")) return ("stone", "quest_sunken_vault");
        return null;
    }

    private bool FurnishDeepRoom(string quest)
    {
        switch (quest)
        {
            case "heart":
                if (QuestSpot('[') is { } hoard) Level.Objects.Add(hoard, QuestItem("heart_of_the_mountain", "heart"));
                if (QuestSpot(')') is { } lair && !KilledUniques.Contains("skorvath_the_cold_drake")) PlaceQuestMonster("skorvath_the_cold_drake", lair, asleep: true);
                Publish(new MessageEvent("The air is cold and smells of old smoke and gold: a dragon lies on its hoard on this level."));
                return true;
            case "watch":
                if (QuestSpot(')') is { } camp && !KilledUniques.Contains("grishnag_the_warchief")) PlaceQuestMonster("grishnag_the_warchief", camp);
                Publish(new MessageEvent("Somewhere a horn sounds, and drums answer it: the orc-host is at the watchtower."));
                return true;
            case "stone":
                if (QuestSpot('[') is { } plinth) Level.Objects.Add(plinth, QuestItem("palantir", "stone"));
                if (QuestSpot(')') is { } keep && !KilledUniques.Contains("the_keeper_of_the_stone")) PlaceQuestMonster("the_keeper_of_the_stone", keep, asleep: true);
                Publish(new MessageEvent("The walls of this level are stained as if water once stood high in them: the drowned vault is here."));
                return true;
        }
        return false;
    }

    /// <summary>What the inn offers for the deep quests (the dwarf and his jewel).</summary>
    private IEnumerable<(string Id, string Label)> DeepInnChoices()
    {
        if (Active("heart") is { Stage: "choose" } && Carrying("heart_of_the_mountain") is not null)
        {
            yield return ("heart:return", "Give the Heart of the Mountain to the dwarf");
            yield return ("heart:keep", "Keep the Heart of the Mountain");
        }
    }

    private bool DeepAtShop(string shopId)
    {
        if (shopId != "bookseller" || Active("stone") is not { Stage: "carry" } || Carrying("palantir") is null) return false;
        AskQuest("The Bookseller", "The White Council's messenger is waiting among the shelves. He sees what you carry, and holds out his hands.",
            ("stone:give", "Give him the palantír"), ("stone:keep", "Keep it"), ("store:bookseller", "Just shop"));
        return true;
    }

    private bool DeepChoice(string[] parts)
    {
        switch (parts[0])
        {
            case "heart" when parts[1] == "return" && Active("heart") is { Stage: "choose" } heart && Carrying("heart_of_the_mountain") is { } jewel:
                TakeQuestItemAway(jewel);
                Player.Gold += 5000;
                Adjust("armoury", -20);
                Adjust("weaponsmith", -10);
                if (Objects.Make(Rng, Math.Max(Player.MaxDepth, 50), good: true, great: true) is { } gift) GiveQuestItem(gift);
                GainExperience(100L * Player.MaxDepth);
                Publish(new MessageEvent("The dwarf takes the Heart in both hands, and for a while says nothing at all. \"Belegost remembers its friends.\""));
                SetStage(heart, "returned");
                return true;
            case "heart" when parts[1] == "keep" && Active("heart") is { Stage: "choose" } kept:
                Publish(new MessageEvent("The dwarf looks at you for a long moment, and leaves without a word. The Heart is warm in your hand."));
                SetStage(kept, "kept");
                return true;
            case "stone" when parts[1] == "give" && Active("stone") is { Stage: "carry" } stone && Carrying("palantir") is { } palantir:
                TakeQuestItemAway(palantir);
                GainExperience(150L * Player.MaxDepth);
                for (var i = 0; i < 2; i++)
                    if (Objects.Make(Rng, Math.Max(Player.MaxDepth, 60), good: true, great: true) is { } reward) GiveQuestItem(reward);
                Publish(new MessageEvent("The messenger wraps the stone in grey cloth. \"The West is in your debt. Rather more than it knows.\""));
                SetStage(stone, "given");
                OpenStoreAfterAsking("bookseller");
                return true;
            case "stone" when parts[1] == "keep" && Active("stone") is { Stage: "carry" } mine:
                Publish(new MessageEvent("The messenger's face closes. \"Then may you never meet what else looks into it.\" He is gone."));
                SetStage(mine, "keep");
                OpenStoreAfterAsking("bookseller");
                return true;
        }
        return false;
    }

    private bool DeepUse(string kindId)
    {
        switch (kindId)
        {
            case "palantir":
                LookIntoThePalantir();
                return true;
            case "heart_of_the_mountain":
                Publish(new MessageEvent(Active("heart") is { Stage: "choose" } ? "The dwarf at the Prancing Pony is waiting for it."
                    : "It glows faintly, and far off, something with wings stirs."));
                return true;
        }
        return false;
    }

    /// <summary>
    /// Looking into the palantír: the whole level mapped and every monster on it seen — and one time
    /// in three the Eye looks back: every monster wakes, and something is sent.
    /// </summary>
    private void LookIntoThePalantir()
    {
        if (AvaQuests.Get("stone") is { } stone) stone.Numbers["looks"] = stone.N("looks") + 1; // (a feat counts them)
        Publish(new MessageEvent("You look into the stone. The dark clears, and you see the whole level laid out, and everything that walks on it."));
        MapArea(255);
        DetectMonsters(255, null);
        UpdateView();
        if (!Rng.OneIn(3)) return;
        Publish(new MessageEvent("And something else sees you. A lidless Eye turns toward you, and the stone burns cold!"));
        foreach (var m in Level.Monsters.All) m.Sleep = 0;
        SummonNearPlayer(2, "HI_UNDEAD", levelBoost: 5);
    }

    private void DeepPickedUp(Item item)
    {
        switch (item.Kind.Id)
        {
            case "heart_of_the_mountain" when Active("heart") is { Stage: "hunt" } heart:
                SetStage(heart, "choose");
                break;
            case "palantir" when Active("stone") is { Stage: "seek" } stone:
                SetStage(stone, "carry");
                break;
        }
    }

    private void DeepMonsterKilled(Monster monster)
    {
        if (monster.Race.Id == "grishnag_the_warchief" && Active("watch") is { Stage: "hold" } watch)
        {
            Publish(new MessageEvent("Grishnag falls, and a howl goes up from the host. They break and run."));
            RelieveTheWatch(watch);
        }
    }

    private void RelieveTheWatch(AvaQuestState watch)
    {
        GainExperience(120L * Player.MaxDepth);
        if (Objects.Make(Rng, Math.Max(Player.MaxDepth, 55), good: true, great: true) is { } gift)
        {
            DropNear(gift, Player.Position);
            Publish(new MessageEvent("The rangers' captain presses something into your hands: \"You held. That's all anyone can do.\""));
        }
        SetStage(watch, "relieved");
    }

    private void DeepUpkeep()
    {
        // The watchtower's siege: counted a turn at a time on its level, a war-band every thirty.
        if (GameTurn % 10 != 0 || Active("watch") is not { Stage: "hold" } watch || Level.Depth != watch.N("depth")) return;
        watch.Numbers["held"] = watch.N("held") + 1;
        var held = watch.N("held");
        if (held >= WatchTurns)
        {
            Publish(new MessageEvent("Horns! The horns of the West answer from the dark, and the host breaks and flees."));
            foreach (var m in Level.Monsters.All.Where(m => !m.Race.IsUnique && (m.Race.Has("ORC") || m.Race.Has("TROLL"))).ToList())
                m.Fear = Math.Max(m.Fear, 200);
            RelieveTheWatch(watch);
            return;
        }
        if (held % 30 != 0) return;
        var depth = Level.Depth;
        var band = 3 + Rng.RandInt0(3);
        var came = 0;
        for (var i = 0; i < band; i++)
        {
            if (FarSpot(12) is not { } spot) break;
            if (_spawner.PickRace(Rng, depth, new HashSet<string>(KilledUniques),
                    r => !r.IsUnique && (r.Has("ORC") || r.Has("TROLL") || r.Has("GIANT")) && r.Depth >= depth - 30, allowOutOfDepth: false) is { } race
                && PlaceQuestMonster(race.Id, spot) is not null)
                came++;
        }
        if (came > 0) Publish(new MessageEvent("Drums: another war-band comes up against the tower!"));
    }
}
