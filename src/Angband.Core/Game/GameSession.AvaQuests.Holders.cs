using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// Two of AVABand's story quests, whose rewards are the potion belt and the scroll case
// (Inventory.BeltKind, Inventory.CaseKind): a character starts without either, and earns them.
//  - The Frozen Cellar (from level 5): the Alchemist's crate of potions, in a cellar gone cold and
//    held by things that like the cold. Bring it to the Alchemy shop for his bandolier.
//  - The Burning Library (from level 8): three of a scribe's folios, in a library held by things
//    that like the fire. Bring them to the Bookseller for his master's scroll case.
// Both grow with you: taken later than their level, the room is deeper (near your deepest, as every
// quest is), its guards are tougher, and more of them stand watch.
public sealed partial class GameSession
{
    /// <summary>The level a quest is first offered at (its minLevel).</summary>
    private int HolderQuestLevel(string id) => QuestDef(id)?.MinLevel ?? 0;

    /// <summary>How far past a holder quest's level you took it (it grows harder by this).</summary>
    private static int HolderLead(AvaQuestState s) => s.N("lead");

    private bool PlanHolderQuest(AvaQuestState s)
    {
        if (s.Id is not ("cellar" or "library")) return false;
        var lead = Math.Max(0, Player.Level - HolderQuestLevel(s.Id));
        s.Numbers["lead"] = lead;
        s.Numbers["depth"] = s.Id == "cellar" ? QuestDepth(1, 5, 60) : QuestDepth(2, 8, 60);
        if (s.Id == "library") s.Numbers["folios"] = 0;
        s.Texts["warning"] = lead >= 8
            ? " A look at you, and a nod. \"You've been deeper than that, I'd guess. Just as well: whatever's in there has had time to grow.\""
            : "";
        s.Stage = "find";
        return true;
    }

    private (string Quest, string Room)? HolderRoomFor(int depth)
    {
        if (Active("cellar") is { Stage: "find" } cellar && depth == cellar.N("depth")) return ("cellar", "quest_frozen_cellar");
        if (Active("library") is { Stage: "find" } library && depth == library.N("depth")) return ("library", "quest_burning_library");
        return null;
    }

    /// <summary>Monsters at home in the cold (or the fire): immune to it, or breathing it.</summary>
    private static bool LikesElement(MonsterRaceDef r, string element) =>
        r.Has(element == "cold" ? "IM_COLD" : "IM_FIRE") || r.Spells.Contains(element == "cold" ? "BR_COLD" : "BR_FIRE");

    private bool FurnishHolderRoom(string quest)
    {
        if (quest is not ("cellar" or "library") || Active(quest) is not { } s) return false;
        if (quest == "cellar")
        {
            if (QuestSpot('(') is { } crate) Level.Objects.Add(crate, QuestItem("alchemists_crate", "cellar"));
        }
        else
        {
            // As many folios as you still need (one taken, a level left, and the shelves keep the rest).
            var need = 3 - (Carrying("scorched_folio", "library")?.Number ?? 0);
            foreach (var shelf in QuestSpots('(').Take(Math.Max(0, need)))
                Level.Objects.Add(shelf, QuestItem("scorched_folio", "library"));
        }
        s.Numbers["guards"] = PlaceHolderGuards(quest == "cellar" ? "cold" : "fire", HolderLead(s));
        Publish(new MessageEvent(quest == "cellar"
            ? "A breath of bitter cold reaches you from somewhere on this level, and the faint chink of glass."
            : "You smell smoke and scorched paper: somewhere on this level, a library is burning."));
        return true;
    }

    /// <summary>
    /// The room's guards: one on each of its posts, chosen from those that like its element where
    /// any will do, and tougher and more of them the further past the quest's level you are. Returns how many.
    /// </summary>
    private int PlaceHolderGuards(string element, int lead)
    {
        var placed = 0;
        var level = Level.Depth + 2 + Math.Min(10, lead / 4);
        var unavailable = new HashSet<string>(KilledUniques);
        bool Fits(MonsterRaceDef r) => !r.IsUnique && !r.Has(MonsterFlags.NeverMove);
        MonsterRaceDef? Pick() =>
            _spawner.PickRace(Rng, level, unavailable, r => Fits(r) && LikesElement(r, element), allowOutOfDepth: false)
            ?? _spawner.PickRace(Rng, level, unavailable, Fits, allowOutOfDepth: false);
        var posts = QuestSpots(')').ToList();
        foreach (var post in posts)
            if (Pick() is { } race && PlaceQuestMonster(race.Id, post, asleep: true) is not null) placed++;
        // More of them, the stronger you are: near the posts, on open floor.
        var extra = Math.Min(6, lead / 5);
        var near = posts.SelectMany(p => Level.AllLocs().Where(l => l.DistanceTo(p) <= 3 && Level.IsEmptyFloor(l) && l != Player.Position))
            .Distinct().ToList();
        for (var i = 0; i < extra && near.Count > 0; i++)
        {
            var at = near[Rng.RandInt0(near.Count)];
            near.Remove(at);
            if (Pick() is { } race && PlaceQuestMonster(race.Id, at, asleep: true) is not null) placed++;
        }
        return placed;
    }

    private void HolderPickedUp(Item item)
    {
        if (item.Kind.Id == "alchemists_crate" && Active("cellar") is { Stage: "find" } cellar) SetStage(cellar, "found");
        if (item.Kind.Id == "scorched_folio" && Active("library") is { Stage: "find" } library)
        {
            var have = Math.Min(3, Carrying("scorched_folio", "library")?.Number ?? 0);
            library.Numbers["folios"] = have;
            if (have >= 3) SetStage(library, "deliver");
            else Publish(new MessageEvent($"A scribe's folio, only a little scorched. ({have} of 3.)"));
        }
    }

    private bool HolderAtShop(string shopId)
    {
        switch (shopId)
        {
            case "alchemist" when Active("cellar") is { Stage: "found" } && Carrying("alchemists_crate", "cellar") is not null:
                AskQuest("The Alchemy shop", "The Alchemist has the crate open before you've set it down, and holds a flask up to the light. "
                                             + "\"Not one cracked. Not one!\"",
                    ("cellar:give", "Hand over the crate"), ("store:alchemist", "Just shop"));
                return true;
            case "bookseller" when Active("library") is { Stage: "deliver" } && Carrying("scorched_folio", "library") is { Number: >= 3 }:
                AskQuest("The Bookseller", "The scribe is here, waiting. He takes the folios from you one at a time, as if they might still be hot.",
                    ("library:give", "Hand over the folios"), ("store:bookseller", "Just shop"));
                return true;
        }
        return false;
    }

    private bool HolderChoice(string[] parts)
    {
        switch (parts[0], parts.ElementAtOrDefault(1))
        {
            case ("cellar", "give") when Active("cellar") is { Stage: "found" } s && Carrying("alchemists_crate", "cellar") is { } crate:
                TakeQuestItemAway(crate);
                HolderReward(s, Inventory.BeltKind, "alchemist",
                    "He unbuckles the bandolier from his own shoulder and holds it out. \"Eight kinds, it holds, and the cold never got a flask through it yet.\"");
                return true;
            case ("library", "give") when Active("library") is { Stage: "deliver" } s && Carrying("scorched_folio", "library") is { } folios:
                TakeQuestItemAway(folios);
                HolderReward(s, Inventory.CaseKind, "bookseller",
                    "He sets a lacquered case of scroll tubes in your hands. \"My master's. He'd want it used, not kept.\"");
                return true;
        }
        return false;
    }

    /// <summary>The reward: the holder itself, gold and experience to match the room you cleared, and the shop's goodwill.</summary>
    private void HolderReward(AvaQuestState s, string holderKind, string shopId, string words)
    {
        Publish(new MessageEvent(words));
        GiveQuestItem(QuestItem(holderKind, s.Id));
        var gold = 50 + 15L * Player.Level;
        Player.Gold += gold;
        GainExperience(25L * Math.Max(1, s.N("depth")));
        Adjust(shopId, -5);
        Publish(new MessageEvent($"You are given {gold} gold, too."));
        SetStage(s, "done");
        OpenStoreAfterAsking(shopId);
    }

    private bool HolderUse(string kindId)
    {
        switch (kindId)
        {
            case Inventory.BeltKind:
                Publish(new MessageEvent($"Your potions ride in the bandolier: up to {Inventory.BeltSize} kinds in one pack slot, half as likely to shatter."));
                return true;
            case Inventory.CaseKind:
                Publish(new MessageEvent($"Your scrolls ride in the case: up to {Inventory.CaseSize} kinds in one pack slot, half as likely to burn."));
                return true;
            case "alchemists_crate":
                Publish(new MessageEvent("Take the crate to the Alchemy shop (5)."));
                return true;
            case "scorched_folio":
                Publish(new MessageEvent("Take the folios to the Bookseller (4): he's waiting for three."));
                return true;
        }
        return false;
    }

    /// <summary>
    /// Loading a character from before the belt and case were earned: what the pack can no longer hold
    /// without them goes to the Home (Butterbur's lads carry it), rather than being lost.
    /// </summary>
    internal void SendOverflowHome(IReadOnlyList<Item> unplaced)
    {
        if (unplaced.Count == 0) return;
        if (_stores.Values.FirstOrDefault(s => s.IsHome) is not { } home)
        {
            foreach (var item in unplaced) DropNear(item, Player.Position); // (no Home: at your feet)
            return;
        }
        foreach (var item in unplaced)
        {
            if (home.Stock.FirstOrDefault(h => h.CanStackWith(item) && h.Number + item.Number <= h.Base.MaxStack) is { } pile) pile.Absorb(item);
            else home.Stock.Add(item);
        }
        var names = unplaced.Select(i => Describe(i)).ToList();
        Publish(new MessageEvent($"Your pack is too full without a potion belt or scroll case, so {string.Join(", ", names)} "
                                 + $"{(names.Count == 1 ? "has" : "have")} been taken to your Home. The Prancing Pony (9) has quests that earn them."));
    }
}
