using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's Arcane Artificer (the town's tenth building, '0', kept by SlatriBartSlow): for a great deal of
// gold he cuts sockets for gems into your gear — body armour and shields up to two, helms, gloves,
// boots, cloaks, weapons and launchers one, bracers one beyond their own; never rings, amulets, lights
// or artifacts. A second socket costs three times the first. One try in ten fails: the piece loses a
// point of enchantment, no socket is cut, and he keeps half his fee. His quest, the Artificer's Chisel,
// changes that: bring his star-forged chisel back and he cuts you one socket free, never fails you again
// and asks half; keep it, and you can cut one socket yourself, even into an artifact (using the chisel:
// GameSession.AvaQuests.Artificer.cs).
public sealed partial class GameSession
{
    /// <summary>The artificer's name.</summary>
    public const string ArtificerName = "SlatriBartSlow";

    /// <summary>One try in this many fails (unless his chisel is home).</summary>
    public const int SocketFailChance = 10;

    /// <summary>The least a first socket costs, before a quarter of the piece's worth.</summary>
    public const int SocketBaseCost = 2500;

    /// <summary>How many sockets the artificer will cut into a piece of this kind (0: none).</summary>
    public static int ArtificerSocketLimit(Item item) => item.Base.Slot switch
    {
        EquipSlot.Body or EquipSlot.Shield => 2,
        EquipSlot.Head or EquipSlot.Hands or EquipSlot.Feet or EquipSlot.Cloak or EquipSlot.Weapon or EquipSlot.Bow => 1,
        EquipSlot.Arms => 1, // (beyond the bracers' own)
        _ => 0,
    };

    /// <summary>Whether the artificer's chisel is home (the quest returned it): free first socket used, half price, no failures.</summary>
    private bool ChiselReturned => AvaQuests.Get("chisel") is { Stage: "returned" } q && q.N("returned") > 0;

    /// <summary>Whether a piece could take another socket (within its kind's limit; an artifact only by your own chisel).</summary>
    public bool CanTakeSocket(Item item, bool byChisel = false) =>
        !item.IsAmmo && item.Number == 1 && (byChisel || !item.IsArtifact) && item.AddedSockets < ArtificerSocketLimit(item);

    /// <summary>What the artificer asks to cut the next socket into a piece.</summary>
    public long SocketCost(Item item)
    {
        if (AvaQuests.Get("chisel") is { Stage: "returned" } q && q.N("freecut") > 0) return 0; // (the first, for the chisel)
        var cost = SocketBaseCost + ItemValue.Of(item, Data) / 4;
        if (item.AddedSockets > 0) cost *= 3;
        if (ChiselReturned) cost /= 2;
        return cost;
    }

    /// <summary>Pieces you wear or carry that he could work on (worn first).</summary>
    private List<Item> SocketablePieces() =>
        [.. Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Where(i => CanTakeSocket(i))];

    /// <summary>
    /// The artificer cuts a socket: paid, then one try in ten fails (not once his chisel is home) —
    /// a point of enchantment lost and half the fee given back. True if a socket was cut.
    /// </summary>
    private bool ArtificerCuts(Item piece, bool free)
    {
        var cost = free ? 0 : SocketCost(piece);
        if (Player.Gold < cost)
        {
            Publish(new MessageEvent($"\"{cost} gold, friend. Come back when you have it.\""));
            return false;
        }
        Player.Gold -= cost;
        var name = ItemNaming.Describe(piece, Knowledge, withArticle: false, full: false);
        if (!free && !ChiselReturned && Rng.OneIn(SocketFailChance))
        {
            if (piece.Base.IsWeapon || piece.Base.Slot == EquipSlot.Bow) { piece.ToHit--; piece.ToDam--; }
            else piece.ToAc--;
            Player.Gold += cost / 2;
            Publish(new MessageEvent($"The chisel skids. {ArtificerName} swears softly. \"Not today, it seems. Your {name} is the worse "
                                     + $"for it, I'm afraid — here's half your gold back.\""));
            RecalculateBonuses();
            return false;
        }
        piece.AddedSockets++;
        Publish(new MessageEvent($"{ArtificerName} works for a long while, and hands back your {name} with a clean new socket."));
        RecalculateBonuses();
        return true;
    }

    /// <summary>The star-forged chisel, kept: one socket cut by your own hand, then it's spent.</summary>
    private void CutSocketYourself(Item piece)
    {
        if (Player.Inventory.Pack.FirstOrDefault(i => i.Kind.Id == "star_forged_chisel") is not { } chisel)
        {
            Publish(new MessageEvent("You have nothing to cut a socket with."));
            return;
        }
        if (!Player.Inventory.Contains(piece) || !CanTakeSocket(piece, byChisel: true))
        {
            Publish(new MessageEvent("That won't take a socket."));
            return;
        }
        piece.AddedSockets++;
        Player.Inventory.Remove(chisel, 1, () => Objects.NextSerial++);
        Publish(new MessageEvent($"The star-forged chisel bites clean, once — and goes dull in your hand. Your "
                                 + $"{ItemNaming.Describe(piece, Knowledge, withArticle: false, full: false)} has a new socket."));
        RecalculateBonuses();
    }
}
