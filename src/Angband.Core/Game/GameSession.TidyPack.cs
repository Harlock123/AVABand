namespace Angband.Core.Game;

/// <summary>AVABand: tidies the pack — stacks that can go together merged, the rest in order. Takes no time.</summary>
public sealed record TidyPackCommand : GameCommand;

// AVABand's "Tidy pack" (Game menu): with bags making pack room something to manage, one command
// merges what can be merged (Angband combine_pack, which happens as things go in, but a kind learned or
// an inscription removed later can leave two stacks that now match), puts the pack in order, and says
// how full it is and how much of it looks like junk (Clear out junk gets rid of that).
public sealed partial class GameSession
{
    private int TidyPack()
    {
        var inv = Player.Inventory;
        var merged = inv.CombinePack();
        var junk = JunkCandidates().Count;
        var room = inv.PackSize - inv.SlotsUsed;
        Publish(new MessageEvent(
            (merged == 0 ? "Your pack is tidy." : $"You tidy your pack: {merged} {(merged == 1 ? "stack" : "stacks")} merged.")
            + $" {inv.SlotsUsed} of {inv.PackSize} slots used ({(room <= 0 ? "full" : room == 1 ? "1 free" : $"{room} free")})."
            + (junk > 0 ? $" {junk} {(junk == 1 ? "thing looks" : "things look")} like junk: Clear out junk will be rid of {(junk == 1 ? "it" : "them")}." : "")));
        return 0;
    }
}
