using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// AVABand's monster trophies (ava_objects.json, base "trophy"; never found lying about, only taken from
// what you kill): a dragon's scale in the colour of its breath (red fire, white cold, blue lightning,
// black acid, green poison), a troll's hide, a great spider's silk. A unique always leaves its trophy;
// others now and then (a dragon one time in three, a spider one in six, a troll one in eight). The
// Armoury's armourer works a trophy into a piece of armour you wear or carry, for gold — the piece
// gains the trophy's resistance (or a troll's healing) — one trophy to a piece, and never an artifact.
public sealed partial class GameSession
{
    /// <summary>A dragon's scale, by the breath it has (the first of these it breathes; a many-coloured one: any of them).</summary>
    private static readonly (string Breath, string Scale)[] DragonScales =
    [
        ("BR_FIRE", "red_dragon_scale"), ("BR_COLD", "white_dragon_scale"), ("BR_ELEC", "blue_dragon_scale"),
        ("BR_ACID", "black_dragon_scale"), ("BR_POIS", "green_dragon_scale"),
    ];

    public static bool IsTrophy(Item item) => item.Base.Id == "trophy";

    /// <summary>The trophies a race can leave (kind ids; none for most), and one time in how many it does.</summary>
    public static (IReadOnlyList<string> Kinds, int OneIn) TrophiesOf(MonsterRaceDef race)
    {
        if (race.Base is "dragon" or "ancient_dragon")
            return ([.. DragonScales.Where(d => race.Spells.Contains(d.Breath)).Select(d => d.Scale)], 3);
        if (race.Base == "spider" && race.Depth >= 15) return (["spider_silk"], 6);
        if (race.Base == "troll") return (["troll_hide"], 8);
        return ([], 0);
    }

    /// <summary>A slain monster's trophy, if it leaves one (always, for a unique).</summary>
    private Item? RollTrophy(Monster monster)
    {
        var (kinds, oneIn) = TrophiesOf(monster.Race);
        if (kinds.Count == 0 || (!monster.Race.IsUnique && !Rng.OneIn(oneIn))) return null;
        var kind = kinds.Count == 1 ? kinds[0] : Rng.Pick(kinds);
        if (Data.Object(kind) is null) return null;
        var trophy = Objects.Create(kind);
        Knowledge.LearnKind(trophy.Kind);
        return trophy;
    }

    /// <summary>What a trophy would give a piece of armour: its resistances and abilities the piece hasn't got.</summary>
    private static List<string> TrophyGifts(Item trophy, Item piece) =>
        [.. trophy.Kind.Resists.Where(r => !piece.Resists.Contains(r)).Concat(trophy.Kind.Flags.Where(f => f != "EASY_KNOW" && !piece.Flags.Contains(f)))];

    /// <summary>Armour a trophy could be worked into: worn first, then carried; not an artifact, nor one already worked.</summary>
    private IEnumerable<Item> TrophyPieces(Item trophy) =>
        Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Where(i =>
            i.Base.Slot is EquipSlot.Body or EquipSlot.Cloak or EquipSlot.Shield or EquipSlot.Head or EquipSlot.Hands or EquipSlot.Feet
                or EquipSlot.Arms
            && !i.IsArtifact && i.Trophy is null && TrophyGifts(trophy, i).Count > 0);

    /// <summary>The trophies you carry that could be worked into something.</summary>
    private List<Item> WorkableTrophies() => [.. Player.Inventory.Pack.Where(t => IsTrophy(t) && TrophyPieces(t).Any())];

    /// <summary>What the armourer charges to work a trophy in: the trophy's worth.</summary>
    public static int TrophyWorkCost(Item trophy) => trophy.Kind.Cost;

    /// <summary>The Armoury's trophy service: which trophy?</summary>
    private void OfferTrophyWork()
    {
        var trophies = WorkableTrophies();
        var choices = trophies.GroupBy(t => t.Kind.Id).Select(g => g.First())
            .Select(t => ($"trophy:pick:{t.Serial}", $"{Capital(Describe(t))} ({TrophyWorkCost(t)} gold)")).ToList();
        choices.Add(("back:armoury", "Back to the shop"));
        AskQuest("The Armoury", trophies.Count == 0
            ? "\"Nothing I can do with what you've brought.\""
            : $"The armourer turns your trophies over with a craftsman's eye. \"I can work one of these into your armour. Which? You have {Player.Gold} gold.\"",
            [.. choices]);
    }

    private void TrophyChoice(string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var trophySerial)
            || Player.Inventory.Pack.FirstOrDefault(i => i.Serial == trophySerial && IsTrophy(i)) is not { } trophy)
        {
            OfferTrophyWork();
            return;
        }
        switch (parts[1])
        {
            case "pick":
                var choices = TrophyPieces(trophy).Select(p =>
                    ($"trophy:work:{trophy.Serial}:{p.Serial}",
                     $"Your {ItemNaming.Describe(p, Knowledge, withArticle: false, full: false)}{(Player.Inventory.Equipped.Contains(p) ? " (worn)" : "")}: "
                     + string.Join(", ", TrophyGifts(trophy, p).Select(TrophyGiftWord)))).ToList();
                choices.Add(("trophy:list:0", "Another trophy"));
                choices.Add(("back:armoury", "Back to the shop"));
                AskQuest("The Armoury", $"\"{Capital(Describe(trophy, withArticle: false))} — good. Into what? {TrophyWorkCost(trophy)} gold, and one to a piece.\"",
                    [.. choices]);
                return;
            case "work" when parts.Length > 3 && long.TryParse(parts[3], out var pieceSerial)
                             && TrophyPieces(trophy).FirstOrDefault(i => i.Serial == pieceSerial) is { } piece:
                var cost = TrophyWorkCost(trophy);
                if (Player.Gold < cost)
                {
                    Publish(new MessageEvent($"\"That'll be {cost} gold, and you haven't got it.\""));
                    break;
                }
                Player.Gold -= cost;
                var gifts = TrophyGifts(trophy, piece);
                foreach (var gift in gifts)
                {
                    if (trophy.Kind.Resists.Contains(gift)) piece.Resists.Add(gift);
                    else piece.Flags.Add(gift);
                }
                piece.Trophy = trophy.Kind.Id;
                Player.Inventory.Remove(trophy, 1, () => Objects.NextSerial++);
                foreach (var rune in piece.Runes().Where(r => !r.StartsWith("curse", StringComparison.Ordinal))) LearnRune(rune);
                Publish(new MessageEvent($"The armourer works {Describe(trophy.Clone(0, 1))} into your {ItemNaming.Describe(piece, Knowledge, withArticle: false, full: false)}: "
                                         + $"it gains {string.Join(" and ", gifts.Select(TrophyGiftWord))}."));
                RecalculateBonuses();
                break;
        }
        if (WorkableTrophies().Count > 0) OfferTrophyWork(); // another? (or back to the shop)
        else OpenStoreAfterAsking("armoury", greet: false);
    }

    private string TrophyGiftWord(string gift) => gift == ItemFlags.Regen ? "regeneration" : AbilityWord(gift);
}
