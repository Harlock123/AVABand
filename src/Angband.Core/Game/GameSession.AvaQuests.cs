using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;
using Angband.Core.World;

namespace Angband.Core.Game;

// AVABand's own quests (the birth option "AVABand's quests"): the Prancing Pony in town offers
// them, and its notice board has smaller jobs. Each story quest puts a room of its own on the level
// that needs it (a key-bearer's hall, a sealed door, a forge, a crypt...), and its items are used
// there: walking into the sealed door with its key, stepping onto the forge or the altar or the
// hermit's door, bringing things back to the shops. Angband's own quests (Sauron and Morgoth) are
// untouched. This file is the frame; the quests themselves are in GameSession.AvaQuests.Stories.cs
// and the board in GameSession.AvaQuests.Board.cs.
public sealed partial class GameSession
{
    /// <summary>This character's AVABand quests.</summary>
    public AvaQuestLog AvaQuests { get; private set; } = new();

    /// <summary>Whether this character has AVABand's quests (the birth option).</summary>
    public bool AvaQuestsOn => Options[OptionIds.AvaQuests];

    internal void RestoreAvaQuests(AvaQuestLog? log) => AvaQuests = log ?? new AvaQuestLog();

    // --- Asking ------------------------------------------------------------------------------------

    private HashSet<string> _questChoices = [];

    /// <summary>Asks the player something; the answers allowed are these, until the next question.</summary>
    private void AskQuest(string title, string text, params (string Id, string Label)[] choices)
    {
        _questChoices = choices.Select(c => c.Id).ToHashSet();
        Publish(new QuestPromptEvent(title, text, [.. choices.Select(c => new QuestChoice(c.Id, c.Label))]));
    }

    /// <summary>An answer: only one the last question offered counts.</summary>
    private int ChooseQuest(string choice)
    {
        if (!_questChoices.Contains(choice)) return 0;
        _questChoices = [];
        var parts = choice.Split(':');
        switch (parts[0])
        {
            case "none": break;
            case "inn": InnChoice(parts.Length > 1 ? parts[1] : ""); break;
            case "offer": ShowOffer(parts[1]); break;
            case "accept": AcceptQuest(parts[1]); break;
            case "board": BoardChoice(parts); break;
            case "store": OpenStoreAfterAsking(parts[1]); break;
            case "back": OpenStoreAfterAsking(parts[1], greet: false); break; // (back from a shop's service)
            case "gem": GemChoice(parts); break;
            case "ident": IdentifyChoice(parts); break;
            case "trophy": TrophyChoice(parts); break;
            case "artificer" or "chisel": ArtificerChoice(parts); break;
            case "armoury": if (parts.ElementAtOrDefault(1) == "trophy") OfferTrophyWork(); else OfferGemRemoval(); break;
            default: StoryChoice(parts); break;
        }
        return 0;
    }

    // --- Quest text ---------------------------------------------------------------------------------

    private AvaQuestDef? QuestDef(string id) => Data.AvaQuest(id);

    /// <summary>A quest's words with its numbers filled in: {keydepth} and {keyfeet}, {depth1} and {feet1}...</summary>
    private static string Fill(string text, AvaQuestState? state)
    {
        if (state is null) return text;
        foreach (var (key, value) in state.Numbers)
        {
            text = text.Replace("{" + key + "}", value.ToString());
            if (key.Contains("depth")) text = text.Replace("{" + key.Replace("depth", "feet") + "}", (value * 50).ToString());
        }
        foreach (var (key, value) in state.Texts) text = text.Replace("{" + key + "}", value);
        return text;
    }

    /// <summary>What the journal says of a quest now.</summary>
    public string QuestText(string id)
    {
        var state = AvaQuests.Get(id);
        var def = QuestDef(id);
        if (state is null || def is null) return "";
        var text = Fill(def.Stages.GetValueOrDefault(state.Stage, ""), state);
        if (id == "thief") text += ThiefJournalLines(state);
        return text;
    }

    /// <summary>The journal: every quest taken or found (the newest first), and the jobs taken from the board.</summary>
    public IReadOnlyList<(string Name, bool Done, string Text)> QuestJournal()
    {
        var list = new List<(string, bool, string)>();
        foreach (var state in AvaQuests.Quests.Values.Reverse())
            if (QuestDef(state.Id) is { } def) list.Add((def.Name, state.IsDone, QuestText(state.Id)));
        foreach (var job in AvaQuests.Board.Where(j => j.Taken))
            list.Add(($"Notice board: {JobTitle(job)}", false, JobText(job)));
        return list;
    }

    private void SetStage(AvaQuestState state, string stage, bool announce = true)
    {
        if (state.Stage.Length > 0 && state.Stage != stage && state.History.LastOrDefault() != state.Stage) state.History.Add(state.Stage);
        state.Stage = stage;
        if (announce && QuestDef(state.Id) is { } def)
        {
            Publish(new MessageEvent(state.IsDone ? $"Quest complete: {def.Name}." : $"Your journal notes something new about {def.Name}."));
            if (state.IsDone) Publish(new AvaQuestCompletedEvent(state.Id, def.Name));
        }
    }

    // --- Taking a quest -----------------------------------------------------------------------------

    /// <summary>The story quests the inn has to offer now: those from your level up, not yet taken.</summary>
    public IEnumerable<AvaQuestDef> QuestOffers() =>
        Data.AvaQuests.Where(q => q.MinLevel > 0 && Player.Level >= q.MinLevel && !AvaQuests.Quests.ContainsKey(q.Id));

    private void ShowOffer(string id)
    {
        if (QuestDef(id) is not { } def || AvaQuests.Quests.ContainsKey(id)) return;
        // The numbers are settled when it is offered (and kept, whether taken now or later).
        var state = PlanQuest(id);
        AskQuest(def.Name, Fill(def.Offer, state), ($"accept:{id}", "Take it on"), ("inn:work", "Not now"));
        _plannedQuest = state;
    }

    private AvaQuestState? _plannedQuest;

    private void AcceptQuest(string id)
    {
        if (QuestDef(id) is not { } def || AvaQuests.Quests.ContainsKey(id)) return;
        var state = _plannedQuest is { } planned && planned.Id == id ? planned : PlanQuest(id);
        _plannedQuest = null;
        AvaQuests.Quests[id] = state;
        Publish(new MessageEvent($"You take on a quest: {def.Name}."));
        OnQuestAccepted(state);
    }

    /// <summary>A depth near your deepest, from a little above to a little below, within the band given.</summary>
    private int QuestDepth(int ahead, int min, int max) => Math.Clamp(Math.Max(Player.MaxDepth, 1) + ahead, min, max);

    // --- The inn ------------------------------------------------------------------------------------

    /// <summary>Walking into the Prancing Pony.</summary>
    private void EnterInn()
    {
        if (IsTutorial)
        {
            TutorialDone.Add("inn");
            AskQuest("The Prancing Pony (a lesson)",
                "In town this is the inn, the 9. Butterbur gives out the story quests your level allows — each has something to find "
                + "and somewhere to use it, and some a choice to make. The notice board has smaller jobs: hunts, things to bring back, "
                + "bounties, scouting. Your quests are kept on the Knowledge screen's Quests page.",
                ("none", "Got it"));
            return;
        }
        if (!AvaQuestsOn)
        {
            Publish(new MessageEvent("The Prancing Pony's common room is quiet: there's no work to be had here."));
            return;
        }
        ReturnLostQuestItems();
        InnChoice("");
    }

    /// <summary>
    /// Leaving a level: quest items left on its floor (a monster's, dropped when the pack was full)
    /// are kept for you, so a quest can't be lost that way; they turn up at the Prancing Pony.
    /// </summary>
    private void KeepLostQuestItems()
    {
        if (!AvaQuestsOn || Level is null) return;
        foreach (var (at, item) in Level.Objects.All.Where(o => o.Item.IsQuestItem && o.Item.QuestTag is not null).ToList())
        {
            // (A quest room's own item is made again with the room; only what a monster dropped is kept.)
            if (item.Kind.Id is not ("key_of_belegost" or "black_market_strongbox" or "journal_page")) continue;
            Level.Objects.Remove(at, item);
            AvaQuests.LostAndFound.Add(new LostQuestItem { Kind = item.Kind.Id, Tag = item.QuestTag! });
        }
    }

    private void ReturnLostQuestItems()
    {
        if (AvaQuests.LostAndFound.Count == 0) return;
        Publish(new MessageEvent("Butterbur hands you a parcel. \"Someone found this down below with your name on it.\""));
        foreach (var lost in AvaQuests.LostAndFound.ToList())
        {
            AvaQuests.LostAndFound.Remove(lost);
            var item = QuestItem(lost.Kind, lost.Tag);
            GiveQuestItem(item);
            if (Player.Inventory.Contains(item) || Carrying(lost.Kind, lost.Tag) is not null) QuestPickedUp(Carrying(lost.Kind, lost.Tag)!);
        }
    }

    private void InnChoice(string what)
    {
        switch (what)
        {
            case "work":
            {
                var offers = QuestOffers().ToList();
                var choices = offers.Select(q => ($"offer:{q.Id}", $"Ask about: {q.Name}")).ToList();
                choices.Add(("inn:", "Back"));
                AskQuest("Work at the Prancing Pony",
                    offers.Count == 0 ? "Nobody has anything for you just now. Come back when you've made more of a name for yourself."
                        : "The common room is busy. A few people look up as you come in; some of them have work.",
                    [.. choices]);
                break;
            }
            case "board":
                ShowBoard();
                break;
            case "leave":
                break;
            default:
            {
                var choices = new List<(string, string)>();
                if (QuestOffers().Any()) choices.Add(("inn:work", "Ask who has work"));
                choices.AddRange(DeepInnChoices());
                foreach (var job in AvaQuests.Board.Where(j => j.Taken && JobComplete(j)))
                    choices.Add(($"board:collect:{job.Id}", $"Collect your pay: {JobTitle(job)}"));
                choices.Add(("inn:board", "Read the notice board"));
                choices.Add(("inn:leave", "Leave"));
                var taken = AvaQuests.Quests.Values.Count(q => !q.IsDone);
                AskQuest("The Prancing Pony",
                    "Firelight, pipe smoke and the smell of stew. Butterbur the innkeeper nods at you over the bar."
                    + (taken > 0 ? $" You have {taken} quest{(taken == 1 ? "" : "s")} under way (see the quest log, Ctrl+J)." : ""),
                    [.. choices]);
                break;
            }
        }
    }

    /// <summary>A shop's own quest business first (the smith's forge, the alchemist, the black market); "Just browse" opens it.</summary>
    private void OpenStoreAfterAsking(string shopId, bool greet = true)
    {
        if (!_stores.TryGetValue(shopId, out var store) || Level.FeatureAt(Player.Position).Shop != shopId) return;
        Publish(new ShopEnteredEvent(store.Id, store.IsHome));
        if (greet) GreetInShop(store);
    }

    /// <summary>Entering a shop: true if a quest has something to ask there first.</summary>
    private bool QuestAtShop(string shopId) => AvaQuestsOn && StoryAtShop(shopId);

    // --- Quest rooms --------------------------------------------------------------------------------

    /// <summary>The quest whose room a new level at this depth gets (one at a time), and the room.</summary>
    private (string Quest, string Room)? _questRoomHere;

    /// <summary>A room that only goes in if the level is one with rooms anyway (the Seal's shrine, which waits for such a level).</summary>
    private bool QuestRoomOptional => _questRoomHere is { Quest: "burden" } && AvaQuests.Get("burden") is null;

    /// <summary>Before a level is made: whether a quest wants its room on it.</summary>
    private (string Quest, string Room)? QuestRoomFor(int depth)
    {
        if (!AvaQuestsOn || depth <= 0 || QuestAt(depth) is not null) return null;
        return StoryRoomFor(depth);
    }

    /// <summary>After a new level is made and peopled: the quest's room furnished, and the quests' other doings.</summary>
    private void OnNewLevel()
    {
        if (!AvaQuestsOn || Level.Depth <= 0) return;
        if (_questRoomHere is { } room) FurnishRoom(room.Quest);
        StoryOnNewLevel();
    }

    /// <summary>A square of the quest room: the first with this symbol.</summary>
    private Loc? QuestSpot(char symbol) => Level.QuestSpots.FirstOrDefault(s => s.Symbol == symbol) is { Symbol: not '\0' } s ? s.Loc : null;

    private void SetQuestFeature(Loc at, string terrainId)
    {
        MoveAside(at); // (a level's own monster standing there: the trapped apprentice was found under one)
        ref var sq = ref Level[at];
        sq.Feature = Data.Terrain[terrainId].Index;
        sq.Trap = 0;
    }

    /// <summary>A monster of this race placed for a quest (awake unless asked otherwise).</summary>
    private Monster? PlaceQuestMonster(string raceId, Loc at, bool asleep = false)
    {
        if (Data.Monster(raceId) is not { } race) return null;
        // A monster of the level's own that happens to stand on the quest's spot steps aside (else the
        // quest's monster would be missing from its room, and the quest stuck for that visit).
        if (!MoveAside(at)) return null;
        var m = _spawner.Place(Level, Rng, race, at, asleep);
        Scheduler.Add(m);
        return m;
    }

    /// <summary>
    /// A monster of the level's own standing on a quest's spot steps aside, to the nearest empty floor;
    /// false if there's nowhere for it to go.
    /// </summary>
    private bool MoveAside(Loc at)
    {
        if (Level.Monsters.At(at) is not { } inTheWay) return true;
        if (Level.AllLocs().Where(l => l != at && Level.IsEmptyFloor(l) && l != Player.Position)
                .OrderBy(l => l.DistanceTo(at)).Select(l => (Loc?)l).FirstOrDefault() is not { } aside)
            return false;
        Level.Monsters.Move(inTheWay, aside);
        return true;
    }

    /// <summary>A quest item, tagged for its quest.</summary>
    private Item QuestItem(string kindId, string tag)
    {
        var item = Objects.Create(kindId);
        item.QuestTag = tag;
        Knowledge.LearnKind(item.Kind);
        return item;
    }

    /// <summary>A random floor square well away from the player, for a quest's wandering monster.</summary>
    private Loc? FarSpot(int distance = 15)
    {
        var spots = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p.DistanceTo(Player.Position) >= distance
                                               && !Level[p].Has(SquareFlags.Vault)).ToList();
        return spots.Count == 0 ? null : spots[Rng.RandInt0(spots.Count)];
    }

    // --- Hooks the game calls ------------------------------------------------------------------------

    /// <summary>Walking into something impassable: a quest's sealed door. True if it was one.</summary>
    private bool QuestBump(Loc at) => AvaQuestsOn && StoryBump(at);

    /// <summary>Having stepped onto a square: a quest's altar, forge or hermit's door.</summary>
    private void QuestStep(Loc at)
    {
        if (AvaQuestsOn) StoryStep(at, Level.FeatureAt(at).Id);
    }

    /// <summary>
    /// A monster dies: its quest items go straight into your pack (so they can't be lost on the
    /// floor), and the quests hear of it.
    /// </summary>
    private void QuestMonsterKilled(Monster monster)
    {
        if (!AvaQuestsOn) return;
        foreach (var item in monster.Carried.Where(i => i.IsQuestItem).ToList())
        {
            monster.Carried.Remove(item);
            if (Player.Inventory.CanCarry(item) && Player.Inventory.Add(item) is { } stack)
            {
                Publish(new MessageEvent($"You take {Describe(stack)} from the body."));
                QuestPickedUp(stack);
            }
            else DropNear(item, monster.Position);
        }
        StoryMonsterKilled(monster);
        BoardMonsterKilled(monster);
    }

    /// <summary>A quest monster that won't die yet (Hathol, while his altar lies profaned). True if it rose again.</summary>
    private bool QuestUndying(Monster monster) => AvaQuestsOn && StoryUndying(monster);

    /// <summary>Picking something up.</summary>
    private void QuestPickedUp(Item item)
    {
        if (AvaQuestsOn && item.QuestTag is not null) StoryPickedUp(item);
    }

    /// <summary>Using (reading, looking at) a quest item.</summary>
    private int UseQuestItem(Item item)
    {
        if (!Player.Inventory.Contains(item))
        {
            Publish(new MessageEvent("You must pick it up first."));
            return 0;
        }
        StoryUse(item);
        return 0;
    }

    /// <summary>Once a game turn: the quests' own upkeep (the Seal's call).</summary>
    private void QuestUpkeep()
    {
        if (AvaQuestsOn) StoryUpkeep();
    }

    /// <summary>A store's prices, as what you've done for (or against) it has made them.</summary>
    private long QuestPrice(Store store, long price) =>
        AvaQuests.PriceAdjust.TryGetValue(store.Id, out var adjust) ? Math.Max(1, price * (100 + adjust) / 100) : price;

    /// <summary>Why a quest item won't leave you (for drop, throw, sell and ignore), or null.</summary>
    private string? QuestItemRefusal(Item item) =>
        item.IsQuestItem ? $"You can't bring yourself to part with {Describe(item)}: you still need it." : null;
}
