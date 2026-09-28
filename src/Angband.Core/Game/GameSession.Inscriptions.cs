using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// Inscriptions (Angband '{' and '}', and auto-inscriptions from the knowledge menu). Writing or
// removing one takes no game time.
public sealed partial class GameSession
{
    /// <summary>Longest inscription kept (Angband's inscription prompt is 80 characters).</summary>
    public const int MaxInscription = 80;

    private int Inscribe(Item item, string text)
    {
        if (!CanReach(item)) return 0;
        text = text.Trim();
        if (text.Length == 0) return Uninscribe(item);
        item.Note = text.Length > MaxInscription ? text[..MaxInscription] : text;
        Publish(new MessageEvent($"You inscribe {Describe(item)}."));
        Player.Inventory.SortQuiver();
        return 0;
    }

    private int Uninscribe(Item item)
    {
        if (!CanReach(item)) return 0;
        if (item.Note is null)
        {
            Publish(new MessageEvent("That item had no inscription to remove."));
            return 0;
        }
        item.Note = null;
        Publish(new MessageEvent("Inscription removed."));
        Player.Inventory.SortQuiver();
        return 0;
    }

    /// <summary>Carried, worn, or on the floor underfoot.</summary>
    private bool CanReach(Item item) => Player.Inventory.Contains(item) || Level.Objects.At(Player.Position).Contains(item);

    /// <summary>
    /// Sets (or with null, clears) the inscription given to every object of a kind as it is noticed
    /// (Angband's auto-inscriptions); carried objects of that kind without one get it at once.
    /// </summary>
    public void SetAutoInscription(ObjectKindDef kind, string? text)
    {
        using var _ = Recorded("inscribe-kind", kind.Id, text);
        Knowledge.SetKindNote(kind, text);
        foreach (var item in Player.Inventory.All) Knowledge.See(item);
        Player.Inventory.SortQuiver();
    }
}
