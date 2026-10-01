using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// AVABand's story quests (their words are in ava_quests.json) — these six, and three more in
// GameSession.AvaQuests.MoreStories.cs (the Apprentice, the Cartographer, the Warden's Fires), and the
// deep ones in GameSession.AvaQuests.DeepStories.cs (the Heart of the Mountain, the Last Watch, the
// Seeing Stone):
//  - The Sealed Door: Durgash the Keybearer's hall; his key opens a sealed dwarven door deeper down.
//  - The Burden: the Seal of Angmar, found in a shrine: speed and strength, but sticky, and it calls
//    monsters; unmade at a dwarven forge further down.
//  - The Broken Blade: three shards in three hoards; the Weapon Smiths reforge them, with two powers
//    chosen from the slays, brands and resistances you know.
//  - Consecration: Hathol, Lord of the Barrow, rises again while his altar lies profaned; the Water
//    of Ulmo, poured on it, lets him die.
//  - The Letter: for a hermit below; read it or not, and deliver it, show it to the Alchemist, or burn
//    it — and the town's shops remember which.
//  - The Thief of the Black Market: ledger pages carried by monsters give clues to which of four
//    suspects took the strongbox; only the thief has it.
public sealed partial class GameSession
{
    // --- Planning a quest (its depths, its thief) ----------------------------------------------------

    private AvaQuestState PlanQuest(string id)
    {
        var s = new AvaQuestState { Id = id };
        switch (id)
        {
            case "sealed_door":
                s.Numbers["keydepth"] = QuestDepth(2, 6, 20);
                s.Numbers["doordepth"] = s.N("keydepth") + 3 + Rng.RandInt0(3);
                s.Stage = "hunt";
                break;
            case "broken_blade":
                s.Numbers["depth1"] = QuestDepth(2, 8, 40);
                s.Numbers["depth2"] = s.N("depth1") + 4 + Rng.RandInt0(3);
                s.Numbers["depth3"] = s.N("depth2") + 4 + Rng.RandInt0(3);
                s.Numbers["found"] = 0;
                s.Stage = "gather";
                break;
            case "consecration":
                s.Numbers["depth"] = QuestDepth(2, 18, 32);
                s.Stage = "hallow";
                break;
            case "letter":
                s.Numbers["depth"] = QuestDepth(3, 8, 25);
                s.Stage = "deliver";
                break;
            case "apprentice" or "cartographer" or "warden" or "heart" or "watch" or "stone":
                PlanMoreQuest(s);
                break;
            case "chisel":
                PlanChiselQuest(s);
                break;
            case "thief":
                var thief = Suspects[Rng.RandInt0(Suspects.Length)];
                s.Texts["thief"] = thief;
                var clues = CluesFor(thief);
                for (var i = 0; i < clues.Count; i++) s.Texts[$"clue{i + 1}"] = clues[i];
                s.Numbers["pages"] = 0;
                s.Stage = "hunt";
                break;
        }
        return s;
    }

    private void OnQuestAccepted(AvaQuestState s)
    {
        switch (s.Id)
        {
            case "consecration": GiveQuestItem(QuestItem("water_of_ulmo", "consecration")); break;
            case "letter": GiveQuestItem(QuestItem("sealed_letter", "letter")); break;
            default: MoreQuestAccepted(s); break;
        }
    }

    /// <summary>Into the pack (or, with no room, at your feet).</summary>
    private void GiveQuestItem(Item item)
    {
        if (Player.Inventory.CanCarry(item) && Player.Inventory.Add(item) is { } stack)
            Publish(new MessageEvent($"You have {Describe(stack)}."));
        else
        {
            DropNear(item, Player.Position);
            Publish(new MessageEvent($"You have no room: {Describe(item)} is at your feet."));
        }
    }

    /// <summary>The first carried or worn item of a quest's with this kind.</summary>
    private Item? Carrying(string kindId, string? tag = null) =>
        Player.Inventory.Pack.Concat(Player.Inventory.Quiver).Concat(Player.Inventory.Equipped)
            .FirstOrDefault(i => i.Kind.Id == kindId && (tag is null || i.QuestTag == tag));

    private void TakeQuestItemAway(Item item)
    {
        Player.Inventory.Remove(item, item.Number, () => Objects.NextSerial++);
        RecalculateBonuses();
    }

    private AvaQuestState? Active(string id) => AvaQuests.Get(id) is { IsDone: false } s ? s : null;

    // --- Rooms ------------------------------------------------------------------------------------------

    /// <summary>The seed-fixed depth from which the Seal of Angmar's shrine may appear (400 to 700 ft).</summary>
    public int BurdenDepth => 8 + (int)(Seed % 7);

    private (string Quest, string Room)? StoryRoomFor(int depth)
    {
        if (Active("sealed_door") is { } door)
        {
            if (door.Stage == "hunt" && depth == door.N("keydepth") && !KilledUniques.Contains("durgash_the_keybearer"))
                return ("sealed_door", "quest_keybearer_hall");
            if (door.Stage == "key" && depth == door.N("doordepth")) return ("sealed_door", "quest_sealed_vault");
        }
        if (Active("burden") is { Stage: "carry" } burden && depth == burden.N("forgedepth")) return ("burden", "quest_forge");
        if (Active("broken_blade") is { Stage: "gather" } blade)
            for (var i = 1; i <= 3; i++)
                if (depth == blade.N($"depth{i}") && blade.N($"got{i}") == 0) return ("broken_blade", "quest_shard_hoard");
        if (Active("consecration") is { } crypt && depth == crypt.N("depth") && !KilledUniques.Contains("hathol_lord_of_the_barrow"))
            return ("consecration", "quest_barrow_crypt");
        if (Active("letter") is { Stage: "deliver" or "opened" } letter && depth == letter.N("depth")) return ("letter", "quest_hermitage");
        if (MoreRoomFor(depth) is { } more) return more;
        // (Last: the other quests' rooms have fixed depths; the Seal's shrine can wait for a level free.)
        // (The shrine gives way to the cartographer's kind of level; it waits for another.)
        if (AvaQuests.Get("burden") is null && depth >= BurdenDepth && QuestProfileFor(depth) is null) return ("burden", "quest_relic_shrine");
        return null;
    }

    private static readonly string[] ShardKinds = ["shard_of_the_hilt", "shard_of_the_blade", "shard_of_the_point"];

    private void FurnishRoom(string quest)
    {
        if (FurnishMoreRoom(quest)) return;
        var feature = QuestSpot('(');
        var monster = QuestSpot(')');
        var item = QuestSpot('[');
        var depth = Level.Depth;
        switch (quest)
        {
            case "sealed_door" when Active("sealed_door")?.Stage == "hunt":
                if (monster is { } m && PlaceQuestMonster("durgash_the_keybearer", m) is { } durgash)
                    durgash.Carried.Add(QuestItem("key_of_belegost", "sealed_door"));
                Publish(new MessageEvent("Somewhere on this level an orc captain is bellowing orders."));
                break;
            case "sealed_door":
                if (feature is { } door) SetQuestFeature(door, "sealed_door");
                if (item is { } treasure && Objects.Make(Rng, depth + 5, good: true, great: true) is { } made) Level.Objects.Add(treasure, made);
                Publish(new MessageEvent("You feel the weight of old stone: the sealed door is somewhere on this level."));
                break;
            case "burden" when AvaQuests.Get("burden") is null:
                if (item is not { } shrine) break; // (not a level with rooms: the shrine waits for one)
                Level.Objects.Add(shrine, QuestItem("seal_of_angmar", "burden"));
                Publish(new MessageEvent("An old malice stirs somewhere on this level."));
                break;
            case "burden":
                if (feature is { } forge) SetQuestFeature(forge, "dwarven_forge");
                Publish(new MessageEvent("You feel the heat of a forge in the rock: the dwarves' fire still burns here."));
                break;
            case "broken_blade" when Active("broken_blade") is { } blade:
            {
                var index = Enumerable.Range(1, 3).First(i => blade.N($"depth{i}") == depth);
                if (item is { } hoard) Level.Objects.Add(hoard, QuestItem(ShardKinds[index - 1], "broken_blade"));
                if (monster is { } g && _spawner.PickRace(Rng, depth + 4, new HashSet<string>(KilledUniques),
                        r => !r.IsUnique && !r.Has(MonsterFlags.NeverMove), allowOutOfDepth: false) is { } race)
                    PlaceQuestMonster(race.Id, g);
                Publish(new MessageEvent("Old steel glints somewhere on this level: a shard of the Broken Blade is here."));
                break;
            }
            case "consecration":
                if (feature is { } altar) SetQuestFeature(altar, "quest_altar");
                if (monster is { } h) PlaceQuestMonster("hathol_lord_of_the_barrow", h, asleep: false);
                Publish(new MessageEvent("A grave-cold dread lies on this level: the Barrow-lord's crypt is here."));
                break;
            case "letter":
                if (feature is { } cell) SetQuestFeature(cell, "hermitage");
                Publish(new MessageEvent("You smell woodsmoke: the hermit's home is somewhere on this level."));
                break;
        }
    }

    // --- Each new level: the thief's pages and suspects -----------------------------------------------

    private static readonly string[] Suspects = ["fengel_the_fence", "nar_the_red_handed", "skarn_quickfingers", "ilse_shadowcloak"];

    private void StoryOnNewLevel()
    {
        if (Active("thief") is not { Stage: "hunt" } thief) return;
        var depth = Level.Depth;
        // A page of the ledger, on some monster (only the next one needed; one level in three, down to 1000 ft).
        if (thief.N("pages") < 3 && depth is >= 3 and <= 20 && Rng.OneIn(3))
        {
            var carriers = Level.Monsters.All.Where(m => !m.Race.IsUnique && !m.Race.Has(MonsterFlags.Questor)).ToList();
            if (carriers.Count > 0)
                carriers[Rng.RandInt0(carriers.Count)].Carried.Add(QuestItem("journal_page", $"thief:page:{thief.N("pages") + 1}"));
        }
        // A suspect, at about their depth, one level in three.
        var here = Suspects.Where(id => !KilledUniques.Contains(id) && Data.Monster(id) is { } r && Math.Abs(r.Depth - depth) <= 3
                                        && Level.Monsters.All.All(m => m.Race.Id != id)).ToList();
        if (here.Count > 0 && Rng.OneIn(3) && FarSpot() is { } spot)
        {
            var who = here[Rng.RandInt0(here.Count)];
            if (PlaceQuestMonster(who, spot, asleep: true) is { } suspect && who == thief.Texts.GetValueOrDefault("thief"))
                suspect.Carried.Add(QuestItem("black_market_strongbox", "thief"));
        }
    }

    // --- The clues -------------------------------------------------------------------------------------

    private static readonly (string Id, string Text, Func<MonsterRaceDef, bool> True)[] Clues =
    [
        ("man", "The thief walked and talked like one of the race of Men.", r => r.Glyph == 'p'),
        ("notman", "Whoever the thief is, it isn't one of the race of Men.", r => r.Glyph != 'p'),
        ("magic", "The thief was heard muttering spells.", r => r.Spells.Count > 0),
        ("nomagic", "The thief used no magic at all.", r => r.Spells.Count == 0),
        ("night", "The thief only ever came by night, and shunned the lamps.", r => r.Has("HURT_LIGHT")),
        ("day", "The thief came and went by daylight, bold as brass.", r => !r.Has("HURT_LIGHT")),
        ("she", "The one witness said \"she\".", r => r.Has("FEMALE")),
        ("he", "The one witness said \"he\".", r => r.Has("MALE")),
        ("beard", "The thief was short and broad, with a beard.", r => r.Glyph == 'h'),
        ("orc", "The thief smelt of orc.", r => r.Has("ORC")),
    ];

    /// <summary>Three clues, each true of the thief, that together fit no other suspect.</summary>
    private List<string> CluesFor(string thiefId)
    {
        var thief = Data.Monster(thiefId)!;
        var others = Suspects.Where(s => s != thiefId).Select(s => Data.Monster(s)!).ToList();
        var fitting = Clues.Where(c => c.True(thief)).ToList();
        for (var tries = 0; tries < 200; tries++)
        {
            var pick = fitting.OrderBy(_ => Rng.RandInt0(1000)).Take(3).ToList();
            if (others.All(o => pick.Any(c => !c.True(o)))) return [.. pick.Select(c => c.Id)];
        }
        return [.. fitting.Take(3).Select(c => c.Id)];
    }

    private static string ClueText(string id) => Clues.FirstOrDefault(c => c.Id == id).Text ?? "";

    private string ThiefJournalLines(AvaQuestState s)
    {
        var pages = s.N("pages");
        if (s.Stage != "hunt") return "";
        if (pages == 0) return " No pages found yet.";
        return " What the pages say: " + string.Join(" ", Enumerable.Range(1, pages).Select(i => ClueText(s.Texts.GetValueOrDefault($"clue{i}", ""))));
    }

    // --- Using things at places ------------------------------------------------------------------------

    private Loc? _questPlace;

    private bool StoryBump(Loc at)
    {
        if (Level.FeatureAt(at).Id != "sealed_door") return false;
        _questPlace = at;
        if (Active("sealed_door") is { Stage: "key" } && Carrying("key_of_belegost") is not null)
            AskQuest("The sealed door", "Dwarven runes run round the lock, and the Key of Belegost is warm in your hand.",
                ("door:unlock", "Unlock the door with the Key of Belegost"), ("none", "Leave it for now"));
        else Publish(new MessageEvent("The door is sealed fast. It was made to open for one key alone."));
        return true;
    }

    private void StoryStep(Loc at, string feature)
    {
        _questPlace = at;
        if (MoreStep(at, feature)) return;
        switch (feature)
        {
            case "dwarven_forge":
                if (Active("burden") is { Stage: "carry" } && Carrying("seal_of_angmar") is not null)
                    AskQuest("The dwarven forge", "The old fire leaps up as you come near, and the Seal of Angmar grows cold against you, as if it knew.",
                        ("forge:unmake", "Cast the Seal of Angmar into the forge"), ("none", "Keep it, for now"));
                else Publish(new MessageEvent("The forge's fire burns on, as it has for an age."));
                break;
            case "quest_altar":
                if (Active("consecration") is { Stage: "hallow" } && Carrying("water_of_ulmo") is not null)
                    AskQuest("The barrow altar", "The altar is cold and filthy; something was sacrificed here, long ago.",
                        ("altar:hallow", "Pour out the Water of Ulmo"), ("none", "Not yet"));
                else if (Active("consecration") is { Stage: "slay" }) Publish(new MessageEvent("The altar shines faintly: it is hallowed."));
                else Publish(new MessageEvent("The altar is cold and profaned."));
                break;
            case "hermitage":
                if (Active("letter") is { Stage: "deliver" or "opened" } letter && (Carrying("sealed_letter") ?? Carrying("opened_letter")) is not null)
                    AskQuest("The hermit's door", letter.Stage == "opened"
                            ? "A thin old man opens the door before you knock, and looks at the broken seal for a long moment."
                            : "A thin old man opens the door before you knock. \"You have something for me.\"",
                        ("hermit:give", "Give him the letter"), ("none", "Keep it"));
                else Publish(new MessageEvent("Nobody answers."));
                break;
        }
    }

    private bool StoryAtShop(string shopId)
    {
        if (MoreAtShop(shopId)) return true;
        switch (shopId)
        {
            case "weaponsmith" when Active("broken_blade") is { Stage: "reforge" } && ShardKinds.All(k => Carrying(k) is not null):
                AskBladePower(null);
                return true;
            case "alchemist" when Active("letter") is { Stage: "opened" } && Carrying("opened_letter") is not null:
                AskQuest("The Alchemy shop", "The alchemist is weighing out powders, and doesn't look up.",
                    ("letter:expose", "Show the alchemist the letter"), ("store:alchemist", "Just shop"));
                return true;
            case "black_market" when Active("thief") is { Stage: "return" } && Carrying("black_market_strongbox") is not null:
                AskQuest("The Black market", "The keeper sees the strongbox under your arm, and for once he smiles.",
                    ("thief:return", "Give back the strongbox"), ("store:black_market", "Just shop"));
                return true;
        }
        return false;
    }

    // --- The Broken Blade's powers -----------------------------------------------------------------------

    private static readonly (string Code, string Name, Action<Item> Add, string Rune)[] BladePowers =
    [
        ("slay_evil", "slay evil", i => i.Slays.Add(new SlayDef { MonsterFlag = "EVIL", Multiplier = 2, Verb = "smite", Name = "evil creatures" }), RuneIds.Slay("EVIL")),
        ("slay_undead", "slay undead", i => i.Slays.Add(new SlayDef { MonsterFlag = "UNDEAD", Multiplier = 3, Verb = "smite", Name = "undead" }), RuneIds.Slay("UNDEAD")),
        ("slay_demon", "slay demons", i => i.Slays.Add(new SlayDef { MonsterFlag = "DEMON", Multiplier = 3, Verb = "smite", Name = "demons" }), RuneIds.Slay("DEMON")),
        ("slay_dragon", "slay dragons", i => i.Slays.Add(new SlayDef { MonsterFlag = "DRAGON", Multiplier = 3, Verb = "smite", Name = "dragons" }), RuneIds.Slay("DRAGON")),
        ("slay_orc", "slay orcs", i => i.Slays.Add(new SlayDef { MonsterFlag = "ORC", Multiplier = 3, Verb = "smite", Name = "orcs" }), RuneIds.Slay("ORC")),
        ("slay_troll", "slay trolls", i => i.Slays.Add(new SlayDef { MonsterFlag = "TROLL", Multiplier = 3, Verb = "smite", Name = "trolls" }), RuneIds.Slay("TROLL")),
        ("slay_giant", "slay giants", i => i.Slays.Add(new SlayDef { MonsterFlag = "GIANT", Multiplier = 3, Verb = "smite", Name = "giants" }), RuneIds.Slay("GIANT")),
        ("slay_animal", "slay animals", i => i.Slays.Add(new SlayDef { MonsterFlag = "ANIMAL", Multiplier = 2, Verb = "smite", Name = "animals" }), RuneIds.Slay("ANIMAL")),
        ("brand_fire", "a fire brand", i => i.Brands.Add(new BrandDef { Element = "fire", Multiplier = 3, Verb = "burn", Name = "fire" }), RuneIds.Brand("fire")),
        ("brand_cold", "a frost brand", i => i.Brands.Add(new BrandDef { Element = "cold", Multiplier = 3, Verb = "freeze", Name = "cold" }), RuneIds.Brand("cold")),
        ("brand_acid", "an acid brand", i => i.Brands.Add(new BrandDef { Element = "acid", Multiplier = 3, Verb = "dissolve", Name = "acid" }), RuneIds.Brand("acid")),
        ("brand_elec", "a lightning brand", i => i.Brands.Add(new BrandDef { Element = "elec", Multiplier = 3, Verb = "shock", Name = "lightning" }), RuneIds.Brand("elec")),
        ("brand_pois", "a poison brand", i => i.Brands.Add(new BrandDef { Element = "pois", Multiplier = 3, Verb = "poison", Name = "poison" }), RuneIds.Brand("pois")),
        ("resist_fire", "resist fire", i => i.Resists.Add("fire"), RuneIds.Resist("fire")),
        ("resist_cold", "resist cold", i => i.Resists.Add("cold"), RuneIds.Resist("cold")),
        ("resist_acid", "resist acid", i => i.Resists.Add("acid"), RuneIds.Resist("acid")),
        ("resist_elec", "resist lightning", i => i.Resists.Add("elec"), RuneIds.Resist("elec")),
        ("resist_pois", "resist poison", i => i.Resists.Add("pois"), RuneIds.Resist("pois")),
    ];

    /// <summary>The powers the smith can give the blade: those whose runes you know, or his own two suggestions if you know fewer.</summary>
    public IReadOnlyList<(string Code, string Name)> BladePowersOffered()
    {
        var known = BladePowers.Where(p => Knowledge.KnowsRune(p.Rune)).Select(p => (p.Code, p.Name)).ToList();
        foreach (var suggestion in new[] { "slay_evil", "brand_fire" })
            if (known.Count < 2 && known.All(k => k.Code != suggestion))
                known.Add((suggestion, BladePowers.First(p => p.Code == suggestion).Name + " (the smith's suggestion)"));
        return known;
    }

    private void AskBladePower(string? first)
    {
        var offered = BladePowersOffered().Where(p => p.Code != first).ToList();
        var choices = offered.Select(p => (first is null ? $"blade:pick:{p.Code}" : $"blade:make:{first}:{p.Code}", $"Give it {p.Name}")).ToList();
        choices.Add(("store:weaponsmith", first is null ? "Not yet: just shop" : "Not yet: keep the shards"));
        AskQuest("The Weapon Smiths", first is null
                ? "The smith lays the three shards out on his bench and whistles. \"I can make her whole. Tell me the first of her powers — something you've learned to call on.\""
                : $"\"{BladePowers.First(p => p.Code == first).Name}, then. And the second?\"",
            [.. choices]);
    }

    private void ReforgeBlade(string first, string second)
    {
        var offered = BladePowersOffered().Select(p => p.Code).ToHashSet();
        if (!offered.Contains(first) || !offered.Contains(second) || first == second) return;
        foreach (var kind in ShardKinds) if (Carrying(kind) is { } shard) TakeQuestItemAway(shard);
        var blade = Objects.Create("reforged_blade");
        blade.ToHit = 10 + Rng.RandInt0(5);
        blade.ToDam = 10 + Rng.RandInt0(5);
        foreach (var code in new[] { first, second })
        {
            var power = BladePowers.First(p => p.Code == code);
            power.Add(blade);
            Knowledge.LearnRune(power.Rune);
        }
        Knowledge.LearnRune(RuneIds.ToHit);
        Knowledge.LearnRune(RuneIds.ToDam);
        blade.Assessed = true;
        blade.QuestTag = "broken_blade";
        Publish(new MessageEvent("The smith works through the night. In the morning the blade lies whole on his bench, bright as the day it was made."));
        GiveQuestItem(blade);
        SetStage(AvaQuests.Get("broken_blade")!, "done");
    }

    // --- Choices -------------------------------------------------------------------------------------------

    private void StoryChoice(string[] parts)
    {
        if (parts.Length > 1 && MoreChoice(parts)) return;
        switch (parts[0])
        {
            case "door" when parts[1] == "unlock" && Active("sealed_door") is { Stage: "key" } door && _questPlace is { } at
                                                      && Level.FeatureAt(at).Id == "sealed_door" && Carrying("key_of_belegost") is { } key:
                TakeQuestItemAway(key);
                SetQuestFeature(at, "open_door");
                Publish(new MessageEvent("The key turns with a sound like a sigh, and the great door swings open."));
                GainExperience(60L * Level.Depth);
                UpdateView();
                SetStage(door, "done");
                break;
            case "forge" when parts[1] == "unmake" && Active("burden") is { Stage: "carry" } burden && Carrying("seal_of_angmar") is { } seal:
                TakeQuestItemAway(seal);
                Publish(new MessageEvent("The Seal of Angmar cracks in the fire with a scream you feel in your teeth, and is gone. You feel lighter."));
                GainExperience(100L * Level.Depth);
                if (Objects.Make(Rng, Level.Depth + 10, good: true, great: true) is { } gift)
                {
                    DropNear(gift, Player.Position);
                    Publish(new MessageEvent("Something bright lies in the ashes."));
                }
                SetStage(burden, "done");
                break;
            case "altar" when parts[1] == "hallow" && Active("consecration") is { Stage: "hallow" } crypt && Carrying("water_of_ulmo") is { } water:
                TakeQuestItemAway(water);
                Publish(new MessageEvent("The water runs over the altar and it shines white. Somewhere near, something shrieks: Hathol can die now."));
                SetStage(crypt, "slay");
                break;
            case "hermit" when parts[1] == "give" && Active("letter") is { } letter && (Carrying("sealed_letter") ?? Carrying("opened_letter")) is { } given:
                TakeQuestItemAway(given);
                Publish(new MessageEvent(letter.Stage == "opened"
                    ? "He takes it. \"Then you know. Keep it to yourself, and this is yours.\""
                    : "He takes it without opening it. \"Good. Here's for your trouble.\""));
                Player.Gold += 40L * Level.Depth;
                for (var i = 0; i < 2; i++) GiveQuestItem(Objects.Create("potion_of_cure_critical_wounds"));
                Adjust("magic", -15);
                SetStage(letter, "delivered");
                break;
            case "letter" when Active("letter") is { } letter2:
                LetterChoice(parts[1], letter2);
                break;
            case "blade" when parts[1] == "pick" && Active("broken_blade") is { Stage: "reforge" }:
                AskBladePower(parts[2]);
                break;
            case "blade" when parts[1] == "make" && parts.Length == 4 && Active("broken_blade") is { Stage: "reforge" }:
                ReforgeBlade(parts[2], parts[3]);
                break;
            case "thief" when parts[1] == "return" && Active("thief") is { Stage: "return" } thief && Carrying("black_market_strongbox") is { } box:
                TakeQuestItemAway(box);
                var pay = 800 + 60L * Player.Level;
                Player.Gold += pay;
                Adjust("black_market", -30);
                Publish(new MessageEvent($"The keeper counts out {pay} gold without a word of complaint, which is a first."));
                SetStage(thief, "done");
                break;
        }
    }

    private void LetterChoice(string what, AvaQuestState letter)
    {
        switch (what)
        {
            case "read" when Carrying("sealed_letter") is { } sealedOne:
                TakeQuestItemAway(sealedOne);
                GiveQuestItem(QuestItem("opened_letter", "letter"));
                Publish(new MessageEvent("You break the seal. \"Friend below: the Alchemist's potions are all the town buys. A little of your "
                                         + "nightshade in his stock and they will buy from me instead. Name your price. — the Magic shop\""));
                SetStage(letter, "opened");
                break;
            case "burn" when (Carrying("sealed_letter") ?? Carrying("opened_letter")) is { } burning:
                TakeQuestItemAway(burning);
                Publish(new MessageEvent("The letter curls and blackens, and is gone."));
                SetStage(letter, "burned");
                break;
            case "expose" when Carrying("opened_letter") is { } shown:
                TakeQuestItemAway(shown);
                Publish(new MessageEvent("The alchemist reads it twice, and goes white, then red. \"I won't forget this.\""));
                Adjust("alchemist", -20);
                Adjust("magic", 25);
                for (var i = 0; i < 3; i++) GiveQuestItem(Objects.Create("cure_serious_wounds"));
                SetStage(letter, "exposed");
                break;
        }
    }

    private void Adjust(string storeId, int percent) =>
        AvaQuests.PriceAdjust[storeId] = AvaQuests.PriceAdjust.GetValueOrDefault(storeId) + percent;

    // --- Using a quest item ---------------------------------------------------------------------------------

    private void StoryUse(Item item)
    {
        if (MoreUse(item.Kind.Id)) return;
        switch (item.Kind.Id)
        {
            case "sealed_letter":
                AskQuest("The sealed letter", "It's addressed to the hermit below the town, and you were asked not to read it.",
                    ("letter:read", "Break the seal and read it"), ("letter:burn", "Burn it unread"), ("none", "Put it away"));
                break;
            case "opened_letter":
                AskQuest("The opened letter", "\"Friend below: the Alchemist's potions are all the town buys. A little of your nightshade in his "
                                              + "stock and they will buy from me instead. Name your price. — the Magic shop\"",
                    ("letter:burn", "Burn it"), ("none", "Put it away"));
                break;
            case "journal_page":
                var n = int.TryParse(item.QuestTag?.Split(':').LastOrDefault(), out var i) ? i : 0;
                var clue = AvaQuests.Get("thief")?.Texts.GetValueOrDefault($"clue{n}");
                Publish(new MessageEvent(clue is null ? "The page is too torn to read." : $"In the margin: {ClueText(clue)}"));
                break;
            case "key_of_belegost":
                Publish(new MessageEvent("It's made for one door: walk into the sealed door with it."));
                break;
            case "water_of_ulmo":
                Publish(new MessageEvent("It must be poured out on Hathol's altar: step onto the altar."));
                break;
            default:
                Publish(new MessageEvent($"{Capitalize(Describe(item))}: {QuestText(item.QuestTag?.Split(':')[0] ?? "")}"));
                break;
        }
    }

    // --- Picking up, killing, dying hard, upkeep --------------------------------------------------------------

    private void StoryPickedUp(Item item)
    {
        DeepPickedUp(item);
        ChiselPickedUp(item);
        switch (item.Kind.Id)
        {
            case "key_of_belegost" when Active("sealed_door") is { Stage: "hunt" } door:
                SetStage(door, "key");
                break;
            case "seal_of_angmar" when AvaQuests.Get("burden") is null:
                var burden = new AvaQuestState { Id = "burden", Stage = "carry" };
                burden.Numbers["forgedepth"] = Math.Min(Level.Depth + 6 + (int)(Seed % 4), Data.Constants.MaxDepth - 2);
                AvaQuests.Quests["burden"] = burden;
                Publish(new MessageEvent("As you take the Seal of Angmar, you feel it look at you. You have found a quest: The Burden."));
                break;
            case "shard_of_the_hilt" or "shard_of_the_blade" or "shard_of_the_point" when Active("broken_blade") is { Stage: "gather" } blade:
                var index = Array.IndexOf(ShardKinds, item.Kind.Id) + 1;
                if (blade.N($"got{index}") != 0) break;
                blade.Numbers[$"got{index}"] = 1;
                blade.Numbers["found"] = blade.N("found") + 1;
                SetStage(blade, blade.N("found") >= 3 ? "reforge" : "gather");
                break;
            case "journal_page" when Active("thief") is { Stage: "hunt" } thief:
                var n = int.TryParse(item.QuestTag?.Split(':').LastOrDefault(), out var i) ? i : 0;
                if (n <= thief.N("pages"))
                {
                    TakeQuestItemAway(item);
                    Publish(new MessageEvent("The page only repeats what you already know; you throw it away."));
                    break;
                }
                thief.Numbers["pages"] = n;
                Publish(new MessageEvent($"In the margin: {ClueText(thief.Texts.GetValueOrDefault($"clue{n}", ""))}"));
                SetStage(thief, "hunt");
                break;
            case "black_market_strongbox" when Active("thief") is { Stage: "hunt" } thief2:
                SetStage(thief2, "return");
                break;
        }
    }

    private void StoryMonsterKilled(Monster monster)
    {
        MoreMonsterKilled(monster);
        var id = monster.Race.Id;
        if (id == "hathol_lord_of_the_barrow" && Active("consecration") is { Stage: "slay" } crypt)
        {
            GainExperience(150L * Level.Depth);
            SetStage(crypt, "done");
        }
        if (Suspects.Contains(id) && Active("thief") is { } thief && thief.Texts.GetValueOrDefault("thief") != id)
            Publish(new MessageEvent($"{monster.Race.Name} had nothing of the Black Market's. Somewhere, the real thief grows wary."));
    }

    private bool StoryUndying(Monster monster)
    {
        if (MoreUndying(monster)) return true;
        if (monster.Race.Id != "hathol_lord_of_the_barrow" || Active("consecration") is not { Stage: "hallow" }) return false;
        monster.Hp = monster.MaxHp;
        if (monster.IsVisible) Publish(new MessageEvent("Hathol, Lord of the Barrow, falls — and rises again from the dust, whole!"));
        return true;
    }

    private void StoryUpkeep()
    {
        MoreUpkeep();
        // The Seal of Angmar, carried and not worn, still calls now and then (worn, its curses do it).
        if (Active("burden") is { Stage: "carry" } && Player.Inventory.Pack.Any(i => i.Kind.Id == "seal_of_angmar") && Rng.OneIn(1500))
        {
            foreach (var m in Level.Monsters.All) m.Sleep = 0;
            Publish(new MessageEvent("The Seal of Angmar calls out, and the dungeon answers."));
        }
    }
}
