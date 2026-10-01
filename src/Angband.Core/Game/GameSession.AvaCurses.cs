using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's curses for its own slots (ava_curses.json; Devouring, the bag's, is in GameSession.Bags.cs):
//  - loose settings (bracers): now and then a stone works loose from its socket and falls at your feet
//    (a cursed stone holds fast);
//  - greed (bags): now and then the bag eats a twentieth of your gold;
//  - lead (bracers, bags): the thing weighs twice what it should (Item.Weight) — felt, and so known,
//    the first time you carry one.
// Each shows itself (its rune learned) when it first acts; Remove Curse takes it off as any curse.
public sealed partial class GameSession
{
    /// <summary>One player turn in this many (on average), bracers with loose settings drop a stone.</summary>
    public const int LooseSettingsChance = 1000;

    /// <summary>One player turn in this many (on average), a greedy bag eats some gold.</summary>
    public const int GreedChance = 600;

    /// <summary>Each player turn: AVABand's curses that act by themselves.</summary>
    private void AvaCurseUpkeep()
    {
        var carried = Player.Inventory.Equipped.Concat(Player.Inventory.Pack).ToList();

        if (carried.FirstOrDefault(i => i.Curses.Contains("loose_settings") && i.Gems.Any(g => g.AddedCurses.Count == 0)) is { } loose
            && Rng.OneIn(LooseSettingsChance))
        {
            var free = loose.Gems.Where(g => g.AddedCurses.Count == 0).ToList();
            var gem = Rng.Pick(free);
            loose.Gems.Remove(gem);
            Merge(loose, gem, -1);
            Publish(new MessageEvent($"A stone works loose from your {Describe(loose, withArticle: false)} and falls: {Describe(gem)}."));
            DropNear(gem, Player.Position);
            LearnRune(RuneIds.Curse("loose_settings"));
            RecalculateBonuses();
        }

        if (Player.Inventory.Pack.FirstOrDefault(i => i.Curses.Contains("greedy")) is { } greedy && Player.Gold > 0
            && Rng.OneIn(GreedChance))
        {
            var eaten = Math.Max(1, Player.Gold / 20);
            Player.Gold -= eaten;
            Publish(new MessageEvent($"Your {Describe(greedy, withArticle: false)} clinks contentedly. {eaten} gold is gone!"));
            LearnRune(RuneIds.Curse("greedy"));
        }

        if (!Knowledge.KnowsRune(RuneIds.Curse("leaden")) && carried.FirstOrDefault(i => i.Curses.Contains("leaden")) is { } leaden)
        {
            Publish(new MessageEvent($"Your {Describe(leaden, withArticle: false)} is unaccountably heavy."));
            LearnRune(RuneIds.Curse("leaden"));
        }
    }
}
