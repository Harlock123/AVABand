using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's own: the potion belt and the scroll case shelter what they hold. A potion in the padded
// belt, or a scroll in the case, is half as likely to be lost to an element that harms it (frost,
// shards, sound or force shattering a potion; fire or acid destroying a scroll).
public sealed partial class GameSession
{
    /// <summary>How much less likely the belt or case makes losing a thing it holds (the chance is divided by this).</summary>
    public const int HolderShelter = 2;

    /// <summary>Whether a thing in the pack rides in the potion belt or scroll case, and so is sheltered.</summary>
    public bool Sheltered(Item item) =>
        (Inventory.InBelt(item) || Inventory.InCase(item)) && Player.Inventory.Pack.Contains(item);
}
