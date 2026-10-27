using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>One of the powers a shrine is hallowed to (AVABand's own).</summary>
/// <param name="Feature">The shrine's terrain (ava_terrain.json).</param>
/// <param name="Name">Who it is hallowed to ("Elbereth").</param>
/// <param name="MinDepth">The shallowest level it is found on.</param>
/// <param name="Weight">How often it is the one found, against the others.</param>
/// <param name="Great">Out of 100 prayers, how many are answered with something lasting (a stat gained).</param>
/// <param name="Blessed">Out of 100, how many more are answered with a blessing (the rest bring a curse).</param>
/// <param name="Gift">What an offering earns, said up front ("the whole level mapped...").</param>
/// <param name="Boons">A prayer's blessings, each an effect string and what is felt.</param>
/// <param name="Banes">A prayer's curses: effect strings, or "drain", "aggravate".</param>
public sealed record ShrinePower(string Id, string Feature, string Name, int MinDepth, int Weight, int Great, int Blessed, string Gift,
    IReadOnlyList<(string Effect, string Text)> Boons, IReadOnlyList<(string Effect, string Text)> Banes)
{
    /// <summary>The dark altar: everything bigger, the risks most of all.</summary>
    public bool IsDark => Id == "morgoth";
}

/// <summary>The shrines this character has used (AVABand's own; kept in the save).</summary>
public sealed class ShrineLog
{
    /// <summary>Shrines prayed at, made an offering to, or desecrated.</summary>
    public int Used { get; set; }
    /// <summary>Shrines desecrated (and lived through it, at the time).</summary>
    public int Desecrated { get; set; }
}

// AVABand's own: shrines. Now and then (about one new level in six or seven, from 100 ft down) a
// level has a shrine in one of its rooms: an altar hallowed to one of the Valar — Elbereth, Aulë,
// Yavanna, Mandos — or, deeper, a dark altar of Morgoth. Stepping onto it (or '_' on it) asks what
// you'll do: pray (a gamble: mostly a blessing, now and then something lasting, sometimes a curse —
// Morgoth's altar gives more and takes far more), make an offering of gold or of something from your
// pack for a gift named up front, desecrate it (a great risk for a great reward), or leave it. Each
// shrine answers once, then goes dark. Where a shrine goes comes from dice of the level's own, so
// nothing else on the level moves; never in the daily dungeon, the weekly challenge or the tutorial,
// nor when playing back a replay recorded before there were shrines.
public sealed partial class GameSession
{
    /// <summary>A new level from this deep may have a shrine (100 ft).</summary>
    public const int ShrineMinDepth = 2;

    /// <summary>A new level has a shrine <see cref="ShrineChance"/> times in <see cref="ShrineOutOf"/>.</summary>
    public const int ShrineChance = 2, ShrineOutOf = 13;

    /// <summary>An offering of gold: this much, and this much more for each level down (twice it at the dark altar).</summary>
    public const int ShrineGoldBase = 100, ShrineGoldPerLevel = 40;

    /// <summary>Out of 100, how often desecrating a shrine pays (the dark altar; any other).</summary>
    public const int DesecrateDarkPays = 55, DesecratePays = 30;

    private static readonly IReadOnlyList<ShrinePower> Powers =
    [
        new("elbereth", "shrine_elbereth", "Elbereth", 2, 3, 4, 76, "the whole level mapped, and everything lying on it sensed",
            [("timed:blessed:1d24+24", "Starlight falls on you, clear and cold."),
             ("timed:prot_evil:1d25+25", "You feel the stars watching over you."),
             ("cure:blind; timed:see_invisible:2d6+50", "Your eyes are opened by the light of the stars."),
             ("light_area; detect_evil:30", "Light blooms about the altar, and evil things are shown to you.")],
            [("timed:blind:1d10+10", "The starlight blazes up, too bright to bear!"),
             ("aggravate", "Your careless prayer rings through the halls."),
             ("timed:slow:1d10+10", "The stars are cold to you; your limbs grow heavy.")]),
        new("aule", "shrine_aule", "Aulë", 2, 3, 4, 71, "a piece of good work, made for one of your calling",
            [("cure:afraid; timed:hero:1d25+25", "The ring of a far hammer fills you with courage."),
             ("timed:oppose_fire:1d10+50; timed:oppose_cold:1d10+50", "You feel as tempered as good steel."),
             ("restore_stat:all", "You feel whole and sound, like new-made work."),
             ("timed:shield:1d20+30", "Your skin feels as hard as forged iron.")],
            [("timed:slow:1d10+10", "A weight like an anvil settles on you."),
             ("aggravate", "A great hammer-stroke rings out, and the whole level hears it."),
             ("drain", "The Smith has no use for idle hands: your strength of life ebbs.")]),
        new("yavanna", "shrine_yavanna", "Yavanna", 2, 3, 4, 76, "every hurt healed and every ill cured, and a full belly",
            [("heal:300:25; cure:poisoned; cure:cut; cure:stun", "Green life flows into you."),
             ("satisfy; cure:poisoned; timed:oppose_pois:1d10+50", "You taste fresh bread and the honey of a far summer."),
             ("cure:blind; cure:confused; cure:image; cure:amnesia; heal:50:15", "The scent of growing things clears your head."),
             ("timed:bold:1d41+79; cure:afraid", "You feel as deep-rooted as an old oak.")],
            [("timed:poisoned:2d7+10", "The moss on the altar is bitter, and you've breathed it in!"),
             ("timed:slow:1d10+10", "Roots seem to tangle about your feet."),
             ("aggravate", "Every creeping thing on the level hears the rustle of your prayer.")]),
        new("mandos", "shrine_mandos", "Mandos", 5, 2, 5, 60, "every rune on what you wear made known, and your life and strength restored",
            [("restore_exp; restore_stat:all", "The Doomsman gives back what was taken from you."),
             ("timed:see_invisible:2d6+50; detect_invisible:30", "The unseen are shown to you, as the dead are to Mandos."),
             ("timed:prot_evil:1d25+25", "A cold, stern guard is set about you."),
             ("timed:telepathy:2d10+20", "For a while you hear the thoughts of the living.")],
            [("drain", "Mandos weighs your deeds and finds them wanting: your life ebbs."),
             ("summon:1d3:UNDEAD", "The dead answer your prayer instead!"),
             ("timed:slow:1d10+10", "The chill of the Halls of Waiting creeps into your limbs.")]),
        new("morgoth", "shrine_morgoth", "Morgoth", 10, 2, 12, 38, "something great, made deep in the dark",
            [("cure:afraid; heal:30:0; timed:berserk:1d25+25", "A red rage fills you."),
             ("timed:fast:1d10+10", "Dark power quickens your blood."),
             ("timed:telepathy:7d7+50", "You hear the thoughts of every creature of the dark."),
             ("timed:oppose_fire:1d10+50; timed:oppose_cold:1d10+50; timed:oppose_acid:1d10+50; timed:oppose_elec:1d10+50; timed:oppose_pois:1d10+50",
                 "The elements no longer seem to touch you.")],
            [("summon:2d3:UNDEAD:5", "The altar's servants come for you!"),
             ("drain", "The Black Foe drinks deep of your life!"),
             ("drain_stat:random", "Something is taken from you, and does not come back."),
             ("aggravate; timed:slow:1d10+10", "Morgoth's laughter rolls through the dark, and everything hears it.")]),
    ];

    /// <summary>Every power a shrine may be hallowed to.</summary>
    public static IReadOnlyList<ShrinePower> ShrinePowers => Powers;

    /// <summary>The power a shrine feature is hallowed to (null for anything else, and a darkened altar).</summary>
    public static ShrinePower? ShrinePowerOf(string featureId) => Powers.FirstOrDefault(p => p.Feature == featureId);

    /// <summary>This character's shrines (kept in the save).</summary>
    public ShrineLog Shrines { get; private set; } = new();

    internal void RestoreShrines(ShrineLog? log) => Shrines = log ?? new ShrineLog();

    /// <summary>Whether there are shrines: the birth option, and never a seeded run, the tutorial, or an older replay.</summary>
    public bool ShrinesOn => Options[OptionIds.AvaShrines] && !Player.IsSeededRun && !IsTutorial && !ShrinesHeld;

    /// <summary>A replay recorded before there were shrines plays back without them (ReplayPlayer sets it).</summary>
    internal bool ShrinesHeld { get; set; }

    // --- Placing ----------------------------------------------------------------------------------

    /// <summary>A new level: now and then a shrine in one of its rooms (from dice of the level's own).</summary>
    private void PlaceShrine()
    {
        if (!ShrinesOn || Level.Depth < ShrineMinDepth || QuestAt(Level.Depth) is not null || InArena) return;
        var dice = new GameRandom(Level.Seed ^ 0x5348_5249_4E45UL);
        if (dice.RandInt0(ShrineOutOf) >= ShrineChance) return;
        var power = PickShrinePower(dice, Level.Depth);
        var spots = ShrineSpots();
        if (spots.Count == 0) return;
        PutShrine(power, spots[dice.RandInt0(spots.Count)]);
    }

    /// <summary>The power a shrine at this depth is hallowed to, by the powers' weights.</summary>
    internal static ShrinePower PickShrinePower(GameRandom dice, int depth)
    {
        var here = Powers.Where(p => depth >= p.MinDepth).ToList();
        var roll = dice.RandInt0(here.Sum(p => p.Weight));
        foreach (var power in here)
            if ((roll -= power.Weight) < 0) return power;
        return here[0];
    }

    /// <summary>Where a shrine may go: an empty floor square in a room (not a vault), with open floor all round it.</summary>
    private List<Loc> ShrineSpots() =>
    [
        .. Level.AllLocs().Where(p => Level[p].Has(SquareFlags.Room) && !Level[p].Has(SquareFlags.Vault) && Level.IsEmptyFloor(p)
                                      && p != Player.Position && !Level.Objects.Any(p)
                                      && Level.Neighbors(p).Count() == 8 && Level.Neighbors(p).All(n => Level.IsFloor(n))),
    ];

    /// <summary>Raises a shrine to a power on a square (tests use it too).</summary>
    internal void PutShrine(ShrinePower power, Loc at)
    {
        Level[at].Feature = Data.Terrain[power.Feature].Index;
        _shrineWatch = null; // (look for it again)
    }

    // --- Seeing ------------------------------------------------------------------------------------

    private Level? _shrineWatchLevel;
    private List<Loc>? _shrineWatch;

    /// <summary>
    /// The shrines to watch for: those on the level you don't remember as shrines when it was arrived on (or loaded),
    /// found before the view is worked out (which remembers whatever comes into it).
    /// </summary>
    private void WatchShrines()
    {
        if (_shrineWatch is not null && _shrineWatchLevel == Level) return;
        _shrineWatchLevel = Level;
        _shrineWatch = [.. Level.AllLocs().Where(p => ShrinePowerOf(Level.FeatureAt(p).Id) is not null && Known.Feature(p) != Level[p].Feature)];
    }

    /// <summary>The first sight of a shrine says whose it is (each watched-for shrine once, as it comes into view).</summary>
    private void NoticeShrines()
    {
        if (_shrineWatch is null || _shrineWatchLevel != Level || _shrineWatch.Count == 0) return;
        foreach (var p in _shrineWatch.Where(p => Level[p].Has(SquareFlags.Seen)).ToList())
        {
            _shrineWatch.Remove(p);
            if (ShrinePowerOf(Level.FeatureAt(p).Id) is not { } power) continue;
            var feature = Data.Terrain[power.Feature];
            // (Its description is for looking at it and for its question: in full here it runs off the message bar.)
            Publish(new MessageEvent($"You see {Article(feature.Name)}. Step onto it to pray or make an offering."));
            Disturb();
        }
    }

    // --- Asking ------------------------------------------------------------------------------------

    private Loc? _shrinePlace;

    /// <summary>Having stepped onto a square (or '_' there): a shrine asks what you'll do. True if it was one.</summary>
    private bool ShrineStep(Loc at)
    {
        var feature = Level.FeatureAt(at);
        if (feature.Id == "shrine_spent")
        {
            Publish(new MessageEvent("The altar is dark and cold. Its power is spent."));
            return true;
        }
        if (ShrinePowerOf(feature.Id) is not { } power) return false;
        _shrinePlace = at;
        AskShrine(power);
        return true;
    }

    /// <summary>What an offering at this shrine costs in gold, here.</summary>
    public int ShrineOfferingGold(ShrinePower power) =>
        (ShrineGoldBase + ShrineGoldPerLevel * Math.Max(1, Level.Depth)) * (power.IsDark ? 2 : 1);

    private void AskShrine(ShrinePower power)
    {
        var feature = Data.Terrain[power.Feature];
        var gold = ShrineOfferingGold(power);
        var choices = new List<(string, string)>
        {
            ("shrine:pray", power.IsDark ? "Pray to Morgoth (great gifts — and great dangers)" : $"Pray to {power.Name} (usually a blessing; sometimes not)"),
            ("shrine:gold", Player.Gold >= gold ? $"Offer {gold} gold, for {power.Gift}" : $"Offer {gold} gold (you have only {Player.Gold})"),
            ("shrine:item", "Offer something from your pack"),
            ("shrine:desecrate", power.IsDark ? "Desecrate the altar (a great risk, for a great reward)" : "Desecrate the shrine (only the boldest would dare)"),
            ("none", "Leave it"),
        };
        AskQuest(Capitalised(feature.Name), $"{feature.Description} An offering of {gold} gold, or of something worth as much, "
            + $"will surely be rewarded with {power.Gift}. It will answer you once.", [.. choices]);
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>What an offered thing is worth to a shrine (as far as you know it, the whole stack).</summary>
    public long ShrineWorth(Item item) => ItemValue.Known(item, Data, Knowledge, item.Number);

    /// <summary>What the shrine makes of an offering worth so much: 0 refused, 1 a blessing, 2 its gift, 3 the gift and a blessing.</summary>
    public int ShrineOfferingTier(ShrinePower power, long worth)
    {
        var gold = ShrineOfferingGold(power);
        return worth >= 3L * gold ? 3 : worth >= gold ? 2 : worth * 2 >= gold ? 1 : 0;
    }

    private static string TierText(int tier) => tier switch
    {
        3 => "its gift, and a blessing",
        2 => "its gift",
        1 => "a blessing",
        _ => "not enough",
    };

    /// <summary>What may be offered: what's in your pack (not quest things).</summary>
    private IEnumerable<Item> Offerable => Player.Inventory.Pack.Where(i => !i.IsQuestItem);

    private void AskShrineItem(ShrinePower power)
    {
        var choices = new List<(string, string)>();
        foreach (var item in Offerable)
        {
            var worth = ShrineWorth(item);
            choices.Add(($"shrine:give:{item.Serial}", $"{Describe(item)} ({worth} gold): {TierText(ShrineOfferingTier(power, worth))}"));
        }
        choices.Add(("shrine:back", "Back"));
        AskQuest($"An offering to {power.Name}", $"Something worth half of {ShrineOfferingGold(power)} gold earns a blessing; worth it all, "
            + $"{power.Gift}; worth three times as much, both. What will you lay on the altar?", [.. choices]);
    }

    private void ShrineChoice(string[] parts)
    {
        if (_shrinePlace is not { } at || Player.Position != at || ShrinePowerOf(Level.FeatureAt(at).Id) is not { } power) return;
        switch (parts.ElementAtOrDefault(1))
        {
            case "pray":
                Pray(power);
                ShrineUsed(at, power);
                break;
            case "gold":
            {
                var gold = ShrineOfferingGold(power);
                if (Player.Gold < gold)
                {
                    Publish(new MessageEvent($"You haven't {gold} gold to offer."));
                    AskShrine(power);
                    return;
                }
                Player.Gold -= gold;
                Publish(new MessageEvent($"You lay {gold} gold on the altar, and it is gone."));
                ShrineGift(power);
                ShrineUsed(at, power);
                break;
            }
            case "item":
                AskShrineItem(power);
                break;
            case "back":
                AskShrine(power);
                break;
            case "give" when long.TryParse(parts.ElementAtOrDefault(2), out var serial)
                             && Offerable.FirstOrDefault(i => i.Serial == serial) is { } item:
            {
                var tier = ShrineOfferingTier(power, ShrineWorth(item));
                if (tier == 0)
                {
                    Publish(new MessageEvent($"The altar does not take {Describe(item)}: it isn't enough."));
                    AskShrineItem(power);
                    return;
                }
                Player.Inventory.Remove(item, item.Number, () => Objects.NextSerial++);
                Player.Inventory.Prune();
                RecalculateBonuses();
                Publish(new MessageEvent($"You lay {Describe(item)} on the altar, and it is gone."));
                if (tier >= 2) ShrineGift(power);
                if (tier != 2) Boon(power);
                ShrineUsed(at, power);
                break;
            }
            case "desecrate":
                Desecrate(power);
                ShrineUsed(at, power, desecrated: true);
                break;
        }
    }

    /// <summary>The shrine has answered: it goes dark, and it is counted.</summary>
    private void ShrineUsed(Loc at, ShrinePower power, bool desecrated = false)
    {
        _shrinePlace = null;
        Level[at].Feature = Data.Terrain["shrine_spent"].Index;
        Known.Remember(Level, at);
        Shrines.Used++;
        if (desecrated && !Player.IsDead) Shrines.Desecrated++;
        if (!Player.IsDead) Publish(new MessageEvent(power.IsDark ? "The altar's red glow dies." : $"The light of {power.Name}'s shrine fades, and it is dark."));
        UpdateView();
    }

    // --- Answers -----------------------------------------------------------------------------------

    /// <summary>A prayer: something lasting, a blessing, or a curse, by the power's odds (the dark altar gives two of what it gives).</summary>
    private void Pray(ShrinePower power)
    {
        Publish(new MessageEvent($"You kneel and pray to {power.Name}."));
        var roll = Rng.RandInt0(100);
        var times = power.IsDark ? 2 : 1;
        if (roll < power.Great)
        {
            var stats = CharacterSpec.StatIds.OrderBy(_ => Rng.RandInt0(1000)).Take(times).ToList();
            Publish(new MessageEvent(power.IsDark ? "Dark power pours into you, and you are changed by it." : $"{power.Name} has heard you, and you are changed."));
            foreach (var stat in stats) ApplyEffects($"restore_stat:{stat}; gain_stat:{stat}");
            Boon(power);
            AddHistory($"Was blessed by {power.Name} at {Feet(Level.Depth)}.");
        }
        else if (roll < power.Great + power.Blessed)
            for (var i = 0; i < times; i++) Boon(power);
        else
        {
            Publish(new MessageEvent(power.IsDark ? "Morgoth heeds you — and he is not kind." : $"{power.Name} is displeased."));
            for (var i = 0; i < times && !Player.IsDead; i++) Bane(power, strong: power.IsDark);
        }
    }

    /// <summary>One of the power's blessings, at random.</summary>
    private void Boon(ShrinePower power)
    {
        var (effect, text) = power.Boons[Rng.RandInt0(power.Boons.Count)];
        Publish(new MessageEvent(text));
        ApplyEffects(effect);
    }

    /// <summary>One of the power's curses, at random (stronger at the dark altar, or for a desecration).</summary>
    private void Bane(ShrinePower power, bool strong)
    {
        var (effect, text) = power.Banes[Rng.RandInt0(power.Banes.Count)];
        Publish(new MessageEvent(text));
        foreach (var e in ItemEffects.Parse(effect))
            switch (e.Name)
            {
                case "drain":
                    ShrineDrain(strong);
                    break;
                case "aggravate":
                    foreach (var m in Level.Monsters.All.Where(m => !m.IsAlly && m.Position.DistanceTo(Player.Position) <= 30)) m.Sleep = 0;
                    Publish(new MessageEvent("You hear things stir all about you."));
                    break;
                case "drain_stat" when e.Arg(0) == "random":
                    DrainStat(Rng.Pick(CharacterSpec.StatIds));
                    break;
                default:
                    ApplyEffect(e);
                    break;
            }
    }

    /// <summary>Experience drained by a curse (hold life keeps most of it).</summary>
    private void ShrineDrain(bool strong)
    {
        var amount = 100 + Player.Experience / (strong ? 10 : 25);
        var holdLife = Player.HasGearFlag(ItemFlags.HoldLife) || Player.Resists.GetValueOrDefault("hold_life") > 0;
        Publish(new MessageEvent(holdLife ? "You feel your life slipping away!" : "You feel your life draining away!"));
        LoseExperience(holdLife ? amount / 10 : amount);
    }

    /// <summary>An offering's gift, the one the shrine names.</summary>
    private void ShrineGift(ShrinePower power)
    {
        switch (power.Id)
        {
            case "elbereth":
                Publish(new MessageEvent("Light like starlight spreads from the altar through the whole level."));
                ApplyEffects("enlightenment");
                break;
            case "aule":
                Publish(new MessageEvent("You hear a hammer ring, far off."));
                GiftItem(Objects.Make(Rng, Level.Depth + 5, good: true, bases: ClassBases()) ?? Objects.Make(Rng, Level.Depth + 5, good: true));
                break;
            case "yavanna":
                Publish(new MessageEvent("You feel the warmth of a summer you never knew."));
                ApplyEffects("heal:5000:100; cure:poisoned; cure:cut; cure:stun; cure:blind; cure:confused; cure:image; cure:amnesia; cure:afraid; cure:slow; satisfy");
                break;
            case "mandos":
            {
                Publish(new MessageEvent("The Doomsman looks on you, and nothing about you is hidden."));
                foreach (var worn in Player.Inventory.Equipped.ToList())
                    foreach (var rune in Knowledge.UnknownRunes(worn).ToList()) LearnRune(rune);
                ApplyEffects("restore_exp; restore_stat:all");
                break;
            }
            case "morgoth":
            {
                Publish(new MessageEvent("Something takes shape on the black iron."));
                var item = Objects.Make(Rng, Level.Depth + 10, good: true, great: true);
                GiftItem(item);
                if (item is not null) AddHistory($"Took {Describe(item)} from the dark altar of Morgoth at {Feet(Level.Depth)}.");
                break;
            }
        }
        RecalculateBonuses();
    }

    /// <summary>A thing given by a shrine, set down beside it.</summary>
    private void GiftItem(Item? item)
    {
        if (item is null)
        {
            Publish(new MessageEvent("Nothing comes of it."));
            return;
        }
        DropNear(item, Player.Position);
        Publish(new MessageEvent("Something lies beside the altar."));
    }

    /// <summary>The kinds of thing that suit your class, for Aulë's gift.</summary>
    private string[] ClassBases() => Player.Class?.Id switch
    {
        "mage" or "necromancer" => ["ring", "amulet", "wand", "staff", "rod", "cloak"],
        "druid" => ["ring", "amulet", "staff", "rod", "cloak", "soft_armour"],
        "priest" => ["hafted", "amulet", "soft_armour", "shield", "helm", "cloak"],
        "paladin" => ["hafted", "sword", "hard_armour", "shield", "helm", "boots"],
        "ranger" => ["bow", "arrow", "soft_armour", "cloak", "boots", "sword"],
        "rogue" => ["sling", "shot", "sword", "soft_armour", "cloak", "gloves"],
        _ => ["sword", "hafted", "polearm", "hard_armour", "shield", "helm", "gloves", "boots"],
    };

    /// <summary>Desecrating a shrine: a great reward, or the power's wrath (the dark altar pays more often).</summary>
    private void Desecrate(ShrinePower power)
    {
        Publish(new MessageEvent(power.IsDark ? "You cast down the dark altar and break its spikes." : $"You deface the shrine of {power.Name}."));
        var pays = Rng.RandInt0(100) < (power.IsDark ? DesecrateDarkPays : DesecratePays);
        if (pays)
        {
            var gold = (power.IsDark ? 150 : 100) * Level.Depth;
            Player.Gold += gold;
            Publish(new MessageEvent($"Hidden in the altar's foot is a hoard: {gold} gold."));
            GiftItem(Objects.Make(Rng, Level.Depth + (power.IsDark ? 10 : 5), good: true, great: power.IsDark));
            if (power.IsDark) GainExperience(10L * Level.Depth * Level.Depth);
        }
        else
        {
            Publish(new MessageEvent(power.IsDark ? "The altar's master is not mocked!" : $"The wrath of {power.Name} falls on you!"));
            if (power.IsDark) ApplyEffects("summon:2d3:UNDEAD:10");
            for (var i = 0; i < 2 && !Player.IsDead; i++) Bane(power, strong: true);
        }
        if (!Player.IsDead)
            AddHistory(power.IsDark ? $"Desecrated a dark altar of Morgoth at {Feet(Level.Depth)}{(pays ? ", and was rewarded" : "")}."
                : $"Desecrated a shrine of {power.Name} at {Feet(Level.Depth)}{(pays ? ", and got away with it" : "")}.");
    }
}
