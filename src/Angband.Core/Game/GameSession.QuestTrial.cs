using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>Debug: try one of AVABand's quests at once (Debug → Try a quest). Recorded, so its replay plays back.</summary>
public sealed record DebugTryQuestCommand(string Quest) : GameCommand;

// Debug → Try a quest: a fresh character raised to a level the quest suits, kitted out for it (as
// tools/balance's bot is: the best of a score of good objects for every slot, chosen for armour and
// the abilities that keep a deep character alive; potions, escapes, food, and a caster's books and
// spells), holding the quest and standing on its level — so a quest can be tried in a minute.
public sealed partial class GameSession
{
    /// <summary>The character level each quest is tried at (about what its depths call for; as the quest bot plays them).</summary>
    public static readonly IReadOnlyDictionary<string, int> QuestTrialLevels = new Dictionary<string, int>
    {
        ["sealed_door"] = 16, ["burden"] = 22, ["broken_blade"] = 24, ["consecration"] = 30, ["letter"] = 18, ["thief"] = 22,
        ["apprentice"] = 22, ["cartographer"] = 12, ["warden"] = 32, ["heart"] = 38, ["watch"] = 40, ["stone"] = 45,
    };

    private static readonly EquipSlot[] TrialSlots =
    [
        EquipSlot.Weapon, EquipSlot.Bow, EquipSlot.Body, EquipSlot.Shield, EquipSlot.Cloak, EquipSlot.Head, EquipSlot.Hands,
        EquipSlot.Feet, EquipSlot.Light, EquipSlot.Amulet, EquipSlot.Ring, EquipSlot.Ring,
    ];

    /// <summary>What a trial character looks for beyond armour: the abilities it doesn't have yet.</summary>
    private static readonly Dictionary<string, int> TrialAbilities = new()
    {
        ["free_act"] = 40, ["see_invis"] = 20, ["pois"] = 20, ["conf"] = 12, ["blind"] = 12, ["fire"] = 8, ["cold"] = 8, ["acid"] = 8,
        ["elec"] = 8, ["hold_life"] = 8, ["nexus"] = 6, ["nether"] = 6, ["chaos"] = 6, ["dark"] = 5, ["light"] = 5, ["sound"] = 5,
        ["shards"] = 5, ["disen"] = 5, ["fear"] = 4,
    };

    private int DebugTryQuest(string questId)
    {
        if (!QuestTrialLevels.TryGetValue(questId, out var level) || AvaQuests.Quests.ContainsKey(questId)) return 0;
        MarkDebugUsed();
        if (Player.Level < level) GainExperience(ExperienceForLevel(level - 1) - Player.Experience);
        KitForTrial(level);
        int depth;
        if (questId == "burden")
            depth = BurdenDepth; // (found, not offered: its Seal lies from here down)
        else
        {
            AcceptQuest(questId);
            var state = AvaQuests.Get(questId)!;
            depth = questId switch
            {
                "sealed_door" => state.N("keydepth"),
                "broken_blade" => state.N("depth1"),
                "cartographer" => state.N("cavern_depth"),
                "thief" => 3,
                _ => state.N("depth"),
            };
        }
        Publish(new MessageEvent($"(Debug) A level-{Player.Level} character, kitted out to try the quest."));
        return DebugJump(Math.Max(1, depth));
    }

    private void KitForTrial(int level)
    {
        // Stats a character of that level would have raised by now (a point for every eight levels, as
        // from potions of Strength and the like), so it can carry and swing what it's given.
        foreach (var stat in CharacterSpec.StatIds)
            Player.NaturalStats[stat] = Math.Min(28, Player.NaturalStats.GetValueOrDefault(stat, 15) + level / 8);
        RecalculateAfterStatChange();
        // The starting kit it's outgrowing goes first (the light stays until a better one comes).
        foreach (var old in Player.Inventory.All.Where(i => i.IsWearable && i.Base.Slot != EquipSlot.Light).ToList())
            Player.Inventory.Remove(old, old.Number, () => Objects.NextSerial++);
        RecalculateBonuses();

        // Gear: the best of twenty good objects made for the level, for each slot, that it can still
        // carry without slowing (leaving room for its potions, scrolls, food and missiles).
        var covered = new HashSet<string>(Player.Race?.Resists ?? []);
        foreach (var slot in TrialSlots)
        {
            if (slot == EquipSlot.Hands && Player.MaxMana > 0) continue; // (gloves hamper most casters)
            var found = new List<Item>();
            for (var i = 0; i < 2000 && found.Count < 20; i++)
                if (Objects.Make(Rng, level, good: true) is { } item && item.Base.Slot == slot && !item.IsCursed
                    && (slot != EquipSlot.Weapon || item.Weight <= 200)
                    && Player.Inventory.TotalWeight + item.Weight + (slot == EquipSlot.Bow ? 120 : 0) <= Player.WeightLimit / 2 - 200)
                    found.Add(item);
            var best = slot switch
            {
                EquipSlot.Weapon => found.MaxBy(i => i.Damage.Count * (i.Damage.Sides + 1) / 2.0 + i.ToDam),
                EquipSlot.Bow => found.MaxBy(i => i.Multiplier * 10 + i.ToDam),
                _ => found.MaxBy(i => i.Armour + i.ToAc + 10 * i.Modifier(ItemModifiers.Speed)
                                      + i.Resists.Where(r => !covered.Contains(r)).Sum(r => TrialAbilities.GetValueOrDefault(r))),
            };
            if (best is null) continue;
            covered.UnionWith(best.Resists);
            Knowledge.LearnKind(best.Kind);
            foreach (var rune in best.Runes()) Knowledge.LearnRune(rune);
            Player.Inventory.Add(best);
            var outgrown = Player.Inventory.InSlot(slot) is { } lit && slot == EquipSlot.Light ? lit : null;
            Wield(best);
            if (outgrown is not null && Player.Inventory.Pack.Contains(outgrown))
                Player.Inventory.Remove(outgrown, outgrown.Number, () => Objects.NextSerial++);
            if (slot == EquipSlot.Bow && Data.Objects.Where(k => Data.ObjectBase(k.Base) is { IsAmmo: true } b && b.AmmoClass == best.Base.AmmoClass)
                    .OrderBy(k => k.Level).FirstOrDefault() is { } ammo)
                GiveKnown(ammo.Id, 40);
        }

        // Supplies for the level.
        var cure = level < 20 ? "potion_of_cure_light_wounds" : level < 40 ? "potion_of_cure_serious_wounds" : level < 45
            ? "potion_of_cure_critical_wounds" : "potion_of_healing";
        GiveKnown(Data.Object(cure) is null ? cure.Replace("potion_of_", "") : cure, 5 + level / 5);
        GiveKnown("phase_door", 10);
        if (level >= 25) GiveKnown("teleportation", 3 + level / 20);
        GiveKnown("scroll_of_word_of_recall", 2);
        GiveKnown("ration_of_food", 8);

        // A caster's books up to its level, and every spell it can learn from them.
        foreach (var book in ClassSpells.Where(s => SpellInfo(s)!.Level <= Player.Level).Select(s => s.Book).Distinct().ToList())
            if (!Player.Inventory.Pack.Any(i => i.Kind.Id == book)) GiveKnown(book, 1);
        for (var tries = 0; tries < 100 && StudyableSpells().FirstOrDefault() is { } spell; tries++)
            if (Study(ChoosesSpells ? spell.Id : null, ChoosesSpells ? null : spell.Book) <= 0) break;

        RecalculateBonuses();
        RecalculateMana();
        Player.Hp = Player.MaxHp;
        Player.Mana = Player.MaxMana;
    }

    private void GiveKnown(string kind, int number)
    {
        if (Data.Object(kind) is null) return;
        var item = Objects.Create(kind, number);
        Knowledge.LearnKind(item.Kind);
        Player.Inventory.Add(item);
    }
}
