using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Generation;
using Angband.Core.Items;
using Angband.Core.Randomness;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Core.Game;

// Angband's options (list-options.h) as they affect play: birth options fixed at character creation,
// interface options the player may change at any time, and cheat options that cost the character
// its place in the score table.
public sealed partial class GameSession
{
    /// <summary>The game's option values (birth, cheat, and the interface options the engine uses).</summary>
    public GameOptions Options { get; private set; } = new();

    /// <summary>
    /// Cheat options ever switched on for this character (Angband's score_* flags). A cheater's
    /// score is not registered.
    /// </summary>
    public HashSet<string> CheatsUsed { get; } = new(StringComparer.Ordinal);

    public bool IsCheater => CheatsUsed.Count > 0;

    /// <summary>What <see cref="CheatsUsed"/> records once a debug command has been used (Angband's NOSCORE_DEBUG).</summary>
    public const string DebugCheat = "debug";

    /// <summary>Whether this character has used a debug command (and so can't enter the high scores).</summary>
    public bool UsedDebug => CheatsUsed.Contains(DebugCheat);

    /// <summary>Marks the character as having used debug commands, for good, as Angband does.</summary>
    public void MarkDebugUsed() => CheatsUsed.Add(DebugCheat);

    /// <summary>
    /// Changes an option during play. Birth options are fixed once the character exists; switching a
    /// cheat on marks the character. Returns false if the change isn't allowed.
    /// </summary>
    public bool SetOption(string id, bool value)
    {
        if (OptionCatalog.Find(id) is not { } def || def.Kind == OptionKind.Birth) return false;
        Options[id] = value;
        if (def.Kind == OptionKind.Cheat && value) CheatsUsed.Add(id);
        if (id == OptionIds.ShowFlavors) Knowledge.ShowFlavors = value;
        return true;
    }

    /// <summary>Takes the interface options from the player's settings (they aren't part of the save).</summary>
    public void ApplyInterfaceOptions(IReadOnlyDictionary<string, bool> values)
    {
        foreach (var o in OptionCatalog.OfKind(OptionKind.Interface))
            if (values.TryGetValue(o.Id, out var v)) SetOption(o.Id, v);
    }

    /// <summary>Loading a save: its birth and cheat options (anything missing takes its default).</summary>
    internal void RestoreOptions(IReadOnlyDictionary<string, bool> values, IEnumerable<string> cheatsUsed)
    {
        Options = new GameOptions(values.Where(kv => OptionCatalog.Find(kv.Key)?.Kind != OptionKind.Interface));
        ApplyOptionDefaults();
        foreach (var c in cheatsUsed) CheatsUsed.Add(c);
        Objects.AllowArtifacts = !Options[OptionIds.NoArtifacts];
    }

    /// <summary>Options the game data can default (constants.json's connectedStairs and noSelling).</summary>
    private void ApplyOptionDefaults()
    {
        if (!Options.IsSet(OptionIds.ConnectStairs)) Options[OptionIds.ConnectStairs] = Data.Constants.ConnectedStairs;
        if (!Options.IsSet(OptionIds.NoSelling)) Options[OptionIds.NoSelling] = Data.Constants.NoSelling;
    }

    /// <summary>Angband birth_no_selling: stores pay nothing, and dungeon gold is increased.</summary>
    public bool NoSelling => Options[OptionIds.NoSelling];

    /// <summary>
    /// Debug: switches birth_no_selling in the middle of a game (birth options are otherwise fixed).
    /// With shops paying, dungeon gold goes back to normal too, as with the option off at birth.
    /// Kept in the save like the other birth options.
    /// </summary>
    public void DebugSetShopsPay(bool pay)
    {
        if (NoSelling != pay) return;
        MarkDebugUsed();
        Options[OptionIds.NoSelling] = !pay;
        Publish(new MessageEvent(pay
            ? "Shops now pay gold for what you sell (debug: \"no selling\" is off)."
            : "Shops now pay nothing again, and dungeon gold is increased (debug: \"no selling\" is on)."));
    }

    // --- Birth: the starting kit and knowledge -----------------------------------------------------------

    /// <summary>
    /// How many of a kit item to give (Angband player_outfit): all of it, or without birth_start_kit
    /// only one food and one light. What isn't given comes as gold (AVABand's kit is otherwise free).
    /// </summary>
    private int KitCount(StartItemDef start, ObjectKindDef kind)
    {
        if (Options[OptionIds.StartKit]) return start.Count;
        var basic = kind.Base is "food" or "light" && _keptBasics.Add(kind.Base);
        var kept = basic ? 1 : 0;
        if (start.Count > kept) _kitGold += ItemValue.Of(Objects.Create(kind), Data) * (start.Count - kept);
        return kept;
    }

    private readonly HashSet<string> _keptBasics = [];
    private long _kitGold;

    /// <summary>Angband birth_know_runes and birth_know_flavors.</summary>
    private void ApplyBirthKnowledge()
    {
        if (Options[OptionIds.KnowRunes])
            foreach (var rune in ObjectInfo.AllRunes(Data)) Knowledge.LearnRune(rune);
        if (Options[OptionIds.KnowFlavors])
            foreach (var kind in Data.Objects.Where(k => Knowledge.Flavor(k) is not null)) Knowledge.LearnKind(kind);
        Knowledge.ShowFlavors = Options[OptionIds.ShowFlavors];
        Objects.AllowArtifacts = !Options[OptionIds.NoArtifacts];
        if (Options[OptionIds.Randarts]) UseRandomArtifacts(GameRandom.DeriveSeed(Seed, 0x52414E44));
    }

    /// <summary>This game's artifacts: the standard set, or with birth_randarts a random one.</summary>
    public IReadOnlyList<ArtifactDef> Artifacts => Objects.Artifacts;

    /// <summary>The seed the random artifacts were made from (null: the standard set).</summary>
    public ulong? RandartSeed { get; private set; }

    /// <summary>Designs this game's random artifact set (Angband do_randart), the same every time for a seed.</summary>
    internal void UseRandomArtifacts(ulong seed)
    {
        RandartSeed = seed;
        Objects.Artifacts = RandartGenerator.Generate(Data, seed);
    }

    // --- Birth: artifacts left behind --------------------------------------------------------------------

    /// <summary>
    /// Leaving a level (Angband generate.c): an artifact left lying there that the player never found
    /// may be generated again later — unless birth_lose_arts is set, when everything left is lost.
    /// </summary>
    private void PreserveUnfoundArtifacts()
    {
        if (Options[OptionIds.LoseArtifacts]) return;
        var left = Level.Objects.All.Select(o => (o.Loc, o.Item))
            .Concat(Level.Monsters.All.SelectMany(m => m.Carried.Select(i => (Loc: m.Position, Item: i))));
        foreach (var (loc, item) in left)
            if (item.Artifact is { } art && !ArtifactFound(item, loc)) Objects.CreatedArtifacts.Remove(art.Id);
    }

    /// <summary>Whether the player has come across an artifact (seen it, or known it for what it is).</summary>
    private bool ArtifactFound(Item item, Geometry.Loc loc) =>
        Knowledge.SeenArtifacts.Contains(item.Artifact!.Id) || Known.RememberedObject(loc) == item;

    // --- Birth: forced descent --------------------------------------------------------------------------

    public bool ForceDescend => Options[OptionIds.ForceDescend];

    /// <summary>
    /// Where going down leads: the next level, or with forced descent one below the deepest level
    /// reached (Angband dungeon_get_next_level from max_depth), stopping at quests.
    /// </summary>
    private int DescentTarget(int from)
    {
        var start = ForceDescend ? Math.Max(from, Player.MaxDepth) : from;
        return CapDepth(from, Math.Min(Data.Constants.MaxDepth, start + 1));
    }

    // --- Interface: pickup and disturbance ----------------------------------------------------------------

    /// <summary>
    /// Angband auto_pickup_okay: how many of a floor object to pick up by itself — everything with
    /// pickup_always, items matching the pack with pickup_inven, and as the <c>=g</c> / <c>!g</c>
    /// inscriptions say (<see cref="Inscription.AutoPickupCount"/>).
    /// </summary>
    private int AutoPickupCount(Item item)
    {
        var matching = Player.Inventory.Pack.Concat(Player.Inventory.Quiver).Where(i => i.CanStackWith(item)).ToList();
        return Inscription.AutoPickupCount(item, Player.Inventory.CanCarry(item) ? item.Number : 0, matching,
            Options[OptionIds.PickupAlways], Options[OptionIds.PickupInven]);
    }

    /// <summary>
    /// What the player can see of the monsters, to tell when something disturbs a repeated command
    /// (rest, travel, tunnelling).
    /// </summary>
    private Dictionary<int, Geometry.Loc> VisibleMonsters() =>
        Level.Monsters.All.Where(m => m.IsVisible).ToDictionary(m => m.Id, m => m.Position);

    /// <summary>
    /// Angband disturb: a monster coming into view always disturbs; one already in view moving
    /// disturbs only with disturb_near.
    /// </summary>
    private bool MonstersDisturb(Dictionary<int, Geometry.Loc> before)
    {
        foreach (var m in Level.Monsters.All.Where(m => m.IsVisible))
        {
            if (!before.TryGetValue(m.Id, out var was)) return true;
            if (Options[OptionIds.DisturbNear] && was != m.Position) return true;
        }
        return false;
    }

    // --- Interface: damage and recharge messages ------------------------------------------------------------

    /// <summary>Angband show_damage: " (12)" after a hit, when the option is on.</summary>
    private string DamageNote(int damage) => Options[OptionIds.ShowDamage] ? $" ({damage})" : "";

    // --- Cheats ---------------------------------------------------------------------------------------------

    /// <summary>Angband cheat_hear and cheat_room: what went into a new level.</summary>
    private void CheatPeek()
    {
        if (Options[OptionIds.CheatRoom] && Level.Depth > 0)
        {
            Publish(new MessageEvent($"Level profile: {Level.ProfileId}."));
            foreach (var vault in Level.Vaults) Publish(new MessageEvent($"Vault: {vault}."));
        }
        if (Options[OptionIds.CheatHear])
            foreach (var m in Level.Monsters.All.Where(m => m.Race.IsUnique).OrderBy(m => m.Id))
                Publish(new MessageEvent($"Unique ({m.Race.Name}) created."));
    }

    /// <summary>
    /// Angband cheat_live: death is refused. The character is healed, cured and sent back to town
    /// (Angband's "cheat death"); the cheat has already cost them their score.
    /// </summary>
    private bool CheatDeath()
    {
        if (!Options[OptionIds.CheatLive]) return false;
        CheatsUsed.Add(OptionIds.CheatLive);
        Publish(new MessageEvent("You invoke wizard mode and cheat death."));
        Player.Hp = Player.MaxHp;
        Player.HpFraction = 0;
        Player.Mana = Player.MaxMana;
        foreach (var (id, _) in Player.Timed.Active.ToList())
            if (Data.Timed(id) is { Harmful: true } def) Player.Timed.Set(def, 0);
        foreach (var stat in Player.StatDrain.Keys.ToList()) Player.StatDrain[stat] = 0;
        Player.Food = Math.Max(Player.Food, Data.Constants.FoodFull - 1);
        Player.RecallTimer = 0;
        Player.DeepDescentTimer = 0;
        RecalculateBonuses();
        if (Player.Depth > 0) ChangeLevel(0, StairArrival.None);
        return true;
    }
}
