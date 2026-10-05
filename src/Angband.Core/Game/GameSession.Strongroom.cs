using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Core.Game;

/// <summary>
/// A locker in Butterbur's strongroom (AVABand's own): one item (or stack), as the save file writes it,
/// left by one of your characters for another. <see cref="Value"/> is its worth when left (the fee to
/// take it out comes from it); <see cref="ItemLevel"/> what a character must be to take it.
/// </summary>
public sealed record StrongroomLocker(string Id, string ItemJson, string Description, string LeftBy, string Date, int ItemLevel, long Value);

/// <summary>
/// The lockers, kept outside any one character's save (the app keeps them beside the high scores); null
/// in replays and tests, where storing just takes the item and the fee, and taking needs the locker's
/// item carried in the command itself.
/// </summary>
public interface IStrongroom
{
    IReadOnlyList<StrongroomLocker> Lockers { get; }
    /// <summary>Puts an item in a locker (giving it its id and date).</summary>
    void Deposit(StrongroomLocker locker);
    /// <summary>Empties a locker; false if it wasn't there (taken already, by another window).</summary>
    bool Remove(string id);
}

/// <summary>Takes a locker's item out of the strongroom (its item carried along, so a replay has it).</summary>
public sealed record TakeFromLockerCommand(string LockerId, string ItemJson, long Value) : GameCommand;

// AVABand's own: Butterbur's strongroom, in the Prancing Pony's cellar. Lockers shared by every character
// you play — an heirloom left by one, for the next. Leaving something costs 50 gold and 5% of its worth;
// taking it out 100 and 10%. Six lockers in all; no artifacts or quest items; and a character takes out
// only what isn't too far past them (an item's level at most twice theirs and five). A birth option
// turns it off, and daily-dungeon characters have none.
public sealed partial class GameSession
{
    /// <summary>How many lockers there are, for all your characters together.</summary>
    public const int StrongroomLockers = 6;

    /// <summary>The lockers (set by the app; null in replays and tests unless a test gives one).</summary>
    public IStrongroom? Strongroom { get; set; }

    /// <summary>Whether this character can use the strongroom: the birth option, the inn's quests, and not a daily-dungeon character.</summary>
    public bool StrongroomOn => Options[OptionIds.Strongroom] && AvaQuestsOn && Player.DailyDate is null;

    /// <summary>What leaving an item costs: 50 gold and 5% of its worth.</summary>
    public long StrongroomDepositFee(Item item) => 50 + ItemWorth(item) / 20;

    /// <summary>What taking an item out costs: 100 gold and 10% of its worth when it was left.</summary>
    public static long StrongroomWithdrawFee(long value) => 100 + value / 10;

    /// <summary>The level an item counts as for taking it out: its kind's, or its ego's if higher.</summary>
    public static int StrongroomLevel(Item item) => Math.Max(item.Kind.Level, item.Ego?.Level ?? 0);

    /// <summary>The highest item level this character may take out: twice their level, and five.</summary>
    public int StrongroomLevelAllowed => Player.Level * 2 + 5;

    private long ItemWorth(Item item) => ItemValue.Of(item, Data) * item.Number;

    /// <summary>Why an item can't go in a locker, or null if it can.</summary>
    public string? StrongroomRefusal(Item item) =>
        item.Artifact is not null ? "Butterbur won't keep an artifact: \"Too much trouble follows things like that.\""
        : item.IsQuestItem ? "That's not yours to leave behind."
        : item.IsGold ? "Butterbur keeps things, not coin."
        : null;

    /// <summary>The strongroom: what you could leave (and its fee), and every locker (and its fee, or the level it needs).</summary>
    private void StrongroomMenu()
    {
        var lockers = Strongroom?.Lockers ?? [];
        var choices = new List<(string, string)>();
        foreach (var locker in lockers)
        {
            var fee = StrongroomWithdrawFee(locker.Value);
            choices.Add(($"strongroom-take:{locker.Id}", locker.ItemLevel > StrongroomLevelAllowed
                ? $"{locker.Description} — left by {locker.LeftBy} (you can take this at level {(locker.ItemLevel - 5 + 1) / 2})"
                : $"Take {locker.Description} — {fee} gold (left by {locker.LeftBy}, {locker.Date})"));
        }
        if (lockers.Count < StrongroomLockers)
            foreach (var item in Player.Inventory.Pack.Concat(Player.Inventory.Equipped).Where(i => StrongroomRefusal(i) is null).Take(20))
                choices.Add(($"strongroom:store:{item.Serial}", $"Leave {Describe(item)}{(Player.Inventory.Equipped.Contains(item) ? " (worn)" : "")} — {StrongroomDepositFee(item)} gold"));
        choices.Add(("inn:", "Back"));
        AskQuest("The strongroom",
            $"Butterbur takes a ring of keys down from a nail and leads you down to the cellar. Iron-bound lockers line the wall — "
            + $"{lockers.Count} of {StrongroomLockers} in use. \"What's put by here stays put by, for you or whoever comes after you. "
            + $"Fifty and a twentieth of its worth to leave a thing; a hundred and a tenth to take one out.\" You have {Player.Gold} gold.",
            [.. choices]);
    }

    private void StrongroomChoice(string[] parts)
    {
        if (!StrongroomOn) return;
        if (parts.ElementAtOrDefault(1) == "store" && long.TryParse(parts.ElementAtOrDefault(2), out var serial)
            && Player.Inventory.All.FirstOrDefault(i => i.Serial == serial) is { } item)
            StoreInLocker(item);
        StrongroomMenu();
    }

    private void StoreInLocker(Item item)
    {
        if (StrongroomRefusal(item) is { } why)
        {
            Publish(new MessageEvent(why));
            return;
        }
        if (Strongroom is { } room && room.Lockers.Count >= StrongroomLockers)
        {
            Publish(new MessageEvent("Every locker is full."));
            return;
        }
        var fee = StrongroomDepositFee(item);
        if (Player.Gold < fee)
        {
            Publish(new MessageEvent($"Butterbur: \"That's {fee} gold to keep it. Come back when you have it.\""));
            return;
        }
        if (Player.Inventory.Equipped.Contains(item) && item.IsSticky)
        {
            Publish(new MessageEvent("It won't come off."));
            return;
        }
        var value = ItemWorth(item);
        var left = Player.Inventory.Remove(item, item.Number, () => Objects.NextSerial++);
        Player.Gold -= fee;
        Strongroom?.Deposit(new StrongroomLocker("", SaveGame.ItemToJson(left), Describe(left),
            $"{Player.Name} the {Player.Race?.Name} {Player.Class?.Name}", "", StrongroomLevel(left), value));
        Publish(new MessageEvent($"Butterbur locks {Describe(left)} away. \"Safe as the Shire.\""));
        RecalculateBonuses();
    }

    private int TakeFromLocker(string lockerId, string itemJson, long value)
    {
        if (!StrongroomOn || Level.FeatureAt(Player.Position).Shop != "inn")
        {
            Publish(new MessageEvent("The strongroom is in the Prancing Pony's cellar."));
            return 0;
        }
        if (SaveGame.ItemFromJson(this, itemJson) is not { } item)
        {
            Publish(new MessageEvent("The locker holds something this world has no place for."));
            return 0;
        }
        if (StrongroomLevel(item) > StrongroomLevelAllowed)
        {
            Publish(new MessageEvent($"Butterbur shakes his head. \"Not yet. That's for someone further along than you — come back at level {(StrongroomLevel(item) - 5 + 1) / 2}.\""));
            return 0;
        }
        var fee = StrongroomWithdrawFee(value);
        if (Player.Gold < fee)
        {
            Publish(new MessageEvent($"Butterbur: \"That's {fee} gold to open it. Come back when you have it.\""));
            return 0;
        }
        if (!Player.Inventory.CanCarry(item))
        {
            Publish(new MessageEvent($"You have no room for {Describe(item)}."));
            return 0;
        }
        // Out of the locker first (a locker taken already, from another window, gives nothing).
        if (Strongroom is { } room && !room.Remove(lockerId))
        {
            Publish(new MessageEvent("That locker stands empty."));
            return 0;
        }
        Player.Gold -= fee;
        Knowledge.LearnKind(item.Kind);
        var held = Player.Inventory.Add(item)!;
        Publish(new MessageEvent($"Butterbur turns the key and hands you {Describe(held)}."));
        RecalculateBonuses();
        if (Level.FeatureAt(Player.Position).Shop == "inn") StrongroomMenu();
        return 0;
    }
}
