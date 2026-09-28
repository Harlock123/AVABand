using System.IO.Compression;
using System.Text.Json;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Sight;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Persistence;

/// <summary>Thrown when a save can't be read: corrupt, from a newer version, or naming data that no longer exists.</summary>
public sealed class SaveGameException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Saves and restores a complete <see cref="GameSession"/>: RNG state, player, inventory, knowledge,
/// the current level (squares, monsters, objects, memory, scent), stores and the scheduler, so a
/// loaded game continues exactly as if it had never stopped. Files are gzipped JSON.
/// </summary>
public static class SaveGame
{
    public const int CurrentVersion = 1;
    public const string Extension = ".avasave";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // --- Public API ------------------------------------------------------------------------------

    public static void Save(GameSession game, Stream stream)
    {
        var file = Capture(game);
        using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gzip, file, Json);
    }

    public static GameSession Load(GameData data, Stream stream) => Restore(data, Read(stream));

    public static SaveSummary ReadSummary(Stream stream) => Read(stream).Summary;

    /// <summary>Writes atomically (to a temporary file, then moved into place).</summary>
    public static void SaveToFile(GameSession game, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) Save(game, stream);
        File.Move(temp, path, overwrite: true);
    }

    public static GameSession LoadFromFile(GameData data, string path)
    {
        using var stream = File.OpenRead(path);
        return Load(data, stream);
    }

    private static SaveFile Read(Stream stream)
    {
        SaveFile? file;
        try
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            file = JsonSerializer.Deserialize<SaveFile>(gzip, Json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            throw new SaveGameException("The save file is damaged and cannot be read.", ex);
        }
        if (file is null) throw new SaveGameException("The save file is empty.");
        if (file.Version > CurrentVersion)
            throw new SaveGameException($"This save is from a newer version of AVABand (format {file.Version}; this version reads {CurrentVersion}).");
        return file;
    }

    // --- Capture ---------------------------------------------------------------------------------

    private static SaveFile Capture(GameSession g)
    {
        var p = g.Player;
        var state = g.Rng.State;
        return new SaveFile
        {
            Version = CurrentVersion,
            Summary = Summary(g),
            Seed = g.Seed,
            TownSeed = g.TownSeed,
            Rng = [state.S0, state.S1, state.S2, state.S3],
            GameTurn = g.GameTurn,
            PendingActors = g.Scheduler.PendingActors.Select(a => a.ActorId).ToList(),
            NextSerial = g.Objects.NextSerial,
            CreatedArtifacts = g.Objects.CreatedArtifacts.Order(StringComparer.Ordinal).ToList(),
            Options = g.Options.OfKind(OptionKind.Birth).Concat(g.Options.OfKind(OptionKind.Cheat)).ToDictionary(kv => kv.Key, kv => kv.Value),
            CheatsUsed = g.CheatsUsed.Order(StringComparer.Ordinal).ToList(),
            RandartSeed = g.RandartSeed,
            StoreDays = g.StoreDays,
            Ignore = new IgnoreSave
            {
                KindsAware = g.Ignore.KindsAware.Order(StringComparer.Ordinal).ToList(),
                KindsUnaware = g.Ignore.KindsUnaware.Order(StringComparer.Ordinal).ToList(),
                Egos = g.Ignore.Egos.Order(StringComparer.Ordinal).ToList(),
                Quality = g.Ignore.Quality.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
            },
            KilledUniques = g.KilledUniques.Order(StringComparer.Ordinal).ToList(),
            QuestKills = new(g.QuestKills),
            CharacterKills = new(g.CharacterKills),
            Knowledge = new KnowledgeSave
            {
                Runes = g.Knowledge.Runes.Order(StringComparer.Ordinal).ToList(),
                AwareKinds = g.Knowledge.AwareKinds.Order(StringComparer.Ordinal).ToList(),
                TriedKinds = g.Knowledge.TriedKinds.Order(StringComparer.Ordinal).ToList(),
                Flavors = g.Knowledge.Flavors.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.Name, kv.Value.Color }),
                SeenKinds = g.Knowledge.SeenKinds.Order(StringComparer.Ordinal).ToList(),
                SeenEgos = g.Knowledge.SeenEgos.Order(StringComparer.Ordinal).ToList(),
                SeenArtifacts = g.Knowledge.SeenArtifacts.Order(StringComparer.Ordinal).ToList(),
                KindNotes = new Dictionary<string, string>(g.Knowledge.KindNotes),
            },
            Player = new PlayerSave
            {
                Name = p.Name, Race = p.Race?.Id, Class = p.Class?.Id, X = p.Position.X, Y = p.Position.Y, Energy = p.Energy,
                Depth = p.Depth, MaxDepth = p.MaxDepth, Level = p.Level, MaxLevel = p.MaxLevel, ExpFactor = p.ExpFactor,
                HitDie = p.HitDie, Experience = p.Experience, ExperienceFraction = p.ExperienceFraction, Gold = p.Gold,
                Food = p.Food, MaxHp = p.MaxHp, Hp = p.Hp, HpFraction = p.HpFraction, IsDead = p.IsDead, IsWinner = p.IsWinner, KilledBy = p.KilledBy,
                Shape = p.Shape, StatScramble = new(p.StatScramble),
                ConHpRemainder = p.ConHpRemainder, Regenerates = p.Regenerates, Infravision = p.Infravision,
                MaxMana = p.MaxMana, Mana = p.Mana, ManaFraction = p.ManaFraction,
                Stats = new(p.Stats), BaseStats = new(p.BaseStats), NaturalStats = new(p.NaturalStats), StatDrain = new(p.StatDrain),
                MaxExperience = p.MaxExperience, HpGains = [.. p.HpGains], RecallTimer = p.RecallTimer, DeepDescentTimer = p.DeepDescentTimer,
                LearnedSpells = [.. p.LearnedSpells], CastSpells = p.CastSpells.Order(StringComparer.Ordinal).ToList(),
                BaseSpeed = p.BaseSpeed, BaseArmour = p.BaseArmour, BaseToHit = p.BaseToHit, BaseToDam = p.BaseToDam,
                BaseStealth = p.BaseStealth, BaseBlows = p.BaseBlows, BaseShots = p.BaseShots,
                IntrinsicResists = new(p.IntrinsicResists),
                Skills = new()
                {
                    ["melee"] = p.SkillMelee, ["bow"] = p.SkillBow, ["throw"] = p.SkillThrow,
                    ["save"] = p.SkillSave, ["disarm"] = p.DisarmSkill, ["device"] = p.SkillDevice,
                },
                Timed = new(p.Timed.Snapshot()),
                Pack = p.Inventory.Pack.Select(ItemToSave).ToList(),
                Quiver = p.Inventory.Quiver.Select(ItemToSave).ToList(),
                Equipment = p.Inventory.Equipment.Select(i => i is null ? null : ItemToSave(i)).ToList(),
            },
            Level = LevelToSave(g, g.Level, g.Known, withScent: true),
            ArenaReturn = g.ArenaReturn is { } back ? LevelToSave(g, back.Level, back.Known, withScent: false) : null,
            ArenaReturnX = g.ArenaReturn?.Position.X ?? 0,
            ArenaReturnY = g.ArenaReturn?.Position.Y ?? 0,
            CommandedMonster = g.Commanded is { IsActive: true } c ? c.Id : 0,
            Stores = g.Stores.Values.Select(s => new StoreSave
            {
                Id = s.Id, Owner = s.Owner?.Name, Stock = s.Stock.Select(ItemToSave).ToList(),
            }).ToList(),
        };
    }

    public static SaveSummary Summary(GameSession g) => new()
    {
        Name = g.Player.Name,
        Race = g.Player.Race?.Name ?? "",
        Class = g.Player.Class?.Name ?? "",
        Level = g.Player.Level,
        Depth = g.Player.Depth,
        MaxDepth = g.Player.MaxDepth,
        Experience = g.Player.Experience,
        GameTurn = g.GameTurn,
        IsDead = g.Player.IsDead,
        IsWinner = g.Player.IsWinner,
        SavedAtUtc = DateTime.UtcNow,
    };

    private static ItemSave ItemToSave(Item i) => new()
    {
        Serial = i.Serial, Kind = i.Kind.Id, Number = i.Number, Damage = i.Damage.ToString(), Armour = i.Armour,
        ToHit = i.ToHit, ToDam = i.ToDam, ToAc = i.ToAc, Fuel = i.Fuel, Charges = i.Charges, Timeout = i.Timeout, ChestState = i.ChestState, GoldValue = i.GoldValue,
        Ego = i.Ego?.Id, Artifact = i.Artifact?.Id, Modifiers = new(i.Modifiers),
        Slays = i.Slays.Select(s => new SlaySave { Flag = s.MonsterFlag, Multiplier = s.Multiplier, Verb = s.Verb, Name = s.Name }).ToList(),
        Brands = i.Brands.Select(b => new BrandSave { Element = b.Element, Multiplier = b.Multiplier, Verb = b.Verb, Name = b.Name }).ToList(),
        Resists = [.. i.Resists], Curses = [.. i.Curses], Flags = [.. i.Flags], OriginDepth = i.OriginDepth, Note = i.Note, Ignored = i.Ignored, Assessed = i.Assessed,
    };

    /// <summary>Objects renamed since older saves: AVABand's first books became Angband 4.2's.</summary>
    private static readonly Dictionary<string, string> RenamedKinds = new(StringComparer.Ordinal)
    {
        ["magic_for_beginners"] = "first_spells", ["conjurings_and_tricks"] = "attacks_and_knowledge",
        ["words_of_wisdom"] = "cleansing_power", ["call_of_the_wild"] = "lesser_charms",
    };

    private static LevelSave LevelToSave(GameSession g, Level level, KnownMap knownMap, bool withScent)
    {
        var terrainIds = level.Terrain.All.Select(t => t.Id).ToList();
        var squares = level.Squares;
        var save = new LevelSave
        {
            Width = level.Width, Height = level.Height, Depth = level.Depth, ProfileId = level.ProfileId, Seed = level.Seed,
            IsLit = level.IsLit, IsKnown = level.IsKnown, Terrain = terrainIds,
            Feeling = level.Feeling, FeelingSquaresSeen = level.FeelingSquaresSeen,
            Decoy = level.Decoy is { } decoy ? decoy.Y * level.Width + decoy.X : null,
            FeelSquares = level.FeelSquares.Select(p => p.Y * level.Width + p.X).Order().ToList(),
            Features = new ushort[squares.Length], Flags = new ushort[squares.Length], Known = new int[squares.Length],
            ScentNow = withScent ? g.Scent.Now : 0, Scent = withScent ? g.Scent.Raw.ToArray() : [],
            NextMonsterId = level.Monsters.NextId,
            Population = [level.Population.Monsters, level.Population.RoomObjects, level.Population.AnywhereObjects, level.Population.Gold],
        };

        // View/Seen/Lit are recomputed after loading; everything else is kept.
        const SquareFlags transient = SquareFlags.View | SquareFlags.Seen | SquareFlags.Lit;
        var known = knownMap.Raw;
        for (var i = 0; i < squares.Length; i++)
        {
            ref var sq = ref squares[i];
            save.Features[i] = sq.Feature;
            save.Flags[i] = (ushort)(sq.Flags & ~transient);
            if (sq.Trap != 0 && g.Data.TrapByIndex(sq.Trap) is { } trap) save.Traps[i] = trap.Id;
            if (sq.LockPower != 0) save.Locks[i] = sq.LockPower;
            save.Known[i] = known[i] == ushort.MaxValue ? -1 : known[i];
        }
        foreach (var (index, item) in knownMap.RememberedObjects) save.RememberedObjects[index] = ItemToSave(item);

        foreach (var m in level.Monsters.All)
        {
            save.Monsters.Add(new MonsterSave
            {
                Id = m.Id, Race = m.Race.Id, X = m.Position.X, Y = m.Position.Y, Hp = m.Hp, MaxHp = m.MaxHp, Speed = m.Speed,
                Energy = m.Energy, Sleep = m.Sleep, Stun = m.Stun, Confused = m.Confused, Fear = m.Fear, Held = m.Held,
                Fast = m.Fast, Slow = m.Slow, Camouflaged = m.Camouflaged, BodyguardOf = m.BodyguardOf, OriginalRace = m.OriginalRace?.Id,
                MimicItem = m.MimicItem is null ? null : ItemToSave(m.MimicItem), Carried = m.Carried.Select(ItemToSave).ToList(), LootRolled = m.LootRolled,
                WanderTarget = m.WanderTarget is { } w ? [w.X, w.Y] : null, WanderStuck = m.WanderStuck,
            });
        }

        foreach (var group in level.Objects.All.GroupBy(o => o.Loc))
            save.Objects.Add(new ObjectPileSave { X = group.Key.X, Y = group.Key.Y, Items = group.Select(o => ItemToSave(o.Item)).ToList() });
        return save;
    }

    // --- Restore ---------------------------------------------------------------------------------

    private static GameSession Restore(GameData data, SaveFile f)
    {
        var errors = new List<string>();
        var g = GameSession.CreateForLoad(data, f.Seed, f.TownSeed);
        if (f.Rng.Length == 4) g.Rng.State = new RandomState(f.Rng[0], f.Rng[1], f.Rng[2], f.Rng[3]);
        g.Objects.NextSerial = f.NextSerial;
        foreach (var a in f.CreatedArtifacts) g.Objects.CreatedArtifacts.Add(a);
        g.RestoreOptions(f.Options ?? [], f.CheatsUsed);
        if (f.RandartSeed is { } randartSeed) g.UseRandomArtifacts(randartSeed);
        var ignore = new IgnoreSettings();
        ignore.KindsAware.UnionWith(f.Ignore.KindsAware);
        ignore.KindsUnaware.UnionWith(f.Ignore.KindsUnaware);
        ignore.Egos.UnionWith(f.Ignore.Egos);
        foreach (var (type, quality) in f.Ignore.Quality)
            if (Enum.TryParse<IgnoreLevel>(quality, out var l)) ignore.Quality[type] = l;
        g.RestoreIgnore(ignore);
        foreach (var u in f.KilledUniques) g.KilledUniques.Add(u);
        foreach (var (q, n) in f.QuestKills) g.QuestKills[q] = n;
        foreach (var (r, n) in f.CharacterKills) g.CharacterKills[r] = n;
        g.Knowledge.Restore(f.Knowledge.Runes, f.Knowledge.AwareKinds, f.Knowledge.TriedKinds,
            f.Knowledge.Flavors.ToDictionary(kv => kv.Key, kv => new FlavorDef { Name = kv.Value[0], Color = kv.Value.ElementAtOrDefault(1) ?? "White" }));
        g.Knowledge.RestoreSeen(f.Knowledge.SeenKinds, f.Knowledge.SeenEgos, f.Knowledge.SeenArtifacts);
        g.Knowledge.RestoreKindNotes(f.Knowledge.KindNotes);

        Item? ToItem(ItemSave s)
        {
            if (data.Object(RenamedKinds.GetValueOrDefault(s.Kind, s.Kind)) is not { } kind || data.ObjectBase(kind.Base) is not { } b)
            {
                errors.Add($"unknown object '{s.Kind}'");
                return null;
            }
            var item = new Item(s.Serial, kind, b, s.Number)
            {
                Damage = Dice.TryParse(s.Damage, out var d) ? d : kind.Damage,
                Armour = s.Armour, ToHit = s.ToHit, ToDam = s.ToDam, ToAc = s.ToAc, Fuel = s.Fuel, GoldValue = s.GoldValue,
                Charges = s.Charges, Timeout = s.Timeout, ChestState = s.ChestState,
                OriginDepth = s.OriginDepth,
                Note = s.Note,
                Ignored = s.Ignored,
                Assessed = s.Assessed,
                Ego = s.Ego is null ? null : data.Egos.FirstOrDefault(e => e.Id == s.Ego),
                Artifact = s.Artifact is null ? null : g.Artifacts.FirstOrDefault(a => a.Id == s.Artifact),
            };
            if (s.Ego is not null && item.Ego is null) errors.Add($"unknown ego '{s.Ego}'");
            if (s.Artifact is not null && item.Artifact is null) errors.Add($"unknown artifact '{s.Artifact}'");
            item.Modifiers.Clear();
            foreach (var (k, v) in s.Modifiers) item.Modifiers[k] = v;
            item.Slays.Clear();
            item.Slays.AddRange(s.Slays.Select(x => new SlayDef { MonsterFlag = x.Flag, Multiplier = x.Multiplier, Verb = x.Verb, Name = x.Name }));
            item.Brands.Clear();
            item.Brands.AddRange(s.Brands.Select(x => new BrandDef { Element = x.Element, Multiplier = x.Multiplier, Verb = x.Verb, Name = x.Name }));
            item.Resists.Clear();
            foreach (var r in s.Resists) item.Resists.Add(r);
            item.Curses.Clear();
            item.Curses.AddRange(s.Curses);
            if (s.Flags is { } flags)
            {
                item.Flags.Clear();
                foreach (var f in flags) item.Flags.Add(f);
            }
            return item;
        }

        // Player.
        var ps = f.Player;
        var p = g.Player;
        p.Name = ps.Name;
        p.Race = ps.Race is null ? null : data.Race(ps.Race);
        p.Class = ps.Class is null ? null : data.Class(ps.Class);
        if (ps.Race is not null && p.Race is null) errors.Add($"unknown race '{ps.Race}'");
        if (ps.Class is not null && p.Class is null) errors.Add($"unknown class '{ps.Class}'");
        p.Position = new Loc(ps.X, ps.Y);
        p.Energy = ps.Energy;
        (p.Depth, p.MaxDepth, p.Level, p.MaxLevel, p.ExpFactor, p.HitDie) = (ps.Depth, ps.MaxDepth, ps.Level, ps.MaxLevel, ps.ExpFactor, ps.HitDie);
        (p.Experience, p.ExperienceFraction, p.Gold, p.Food) = (ps.Experience, ps.ExperienceFraction, ps.Gold, ps.Food);
        (p.MaxHp, p.Hp, p.HpFraction, p.IsDead, p.KilledBy) = (ps.MaxHp, ps.Hp, ps.HpFraction, ps.IsDead, ps.KilledBy);
        p.IsWinner = ps.IsWinner;
        p.Shape = ps.Shape;
        foreach (var (k, v) in ps.StatScramble) p.StatScramble[k] = v;
        (p.ConHpRemainder, p.Regenerates, p.Infravision) = (ps.ConHpRemainder, ps.Regenerates, ps.Infravision);
        (p.MaxMana, p.Mana, p.ManaFraction) = (ps.MaxMana, ps.Mana, ps.ManaFraction);
        foreach (var (k, v) in ps.Stats) p.Stats[k] = v;
        foreach (var (k, v) in ps.BaseStats) p.BaseStats[k] = v;
        // Saves from before gear stat bonuses: the current stats were the natural ones.
        foreach (var (k, v) in ps.NaturalStats.Count > 0 ? ps.NaturalStats : ps.Stats) p.NaturalStats[k] = v;
        foreach (var (k, v) in ps.StatDrain) p.StatDrain[k] = v;
        p.MaxExperience = Math.Max(ps.MaxExperience, ps.Experience);
        p.HpGains.AddRange(ps.HpGains);
        (p.RecallTimer, p.DeepDescentTimer) = (ps.RecallTimer, ps.DeepDescentTimer);
        // Spells since retired (AVABand's pre-4.2 books) are forgotten.
        p.LearnedSpells.AddRange(ps.LearnedSpells.Where(id => data.Spell(id) is not null));
        p.CastSpells.UnionWith(ps.CastSpells.Where(id => data.Spell(id) is not null));
        (p.BaseSpeed, p.BaseArmour, p.BaseToHit, p.BaseToDam) = (ps.BaseSpeed, ps.BaseArmour, ps.BaseToHit, ps.BaseToDam);
        (p.BaseStealth, p.BaseBlows, p.BaseShots) = (ps.BaseStealth, ps.BaseBlows, ps.BaseShots);
        foreach (var (k, v) in ps.IntrinsicResists) p.IntrinsicResists[k] = v;
        p.SkillMelee = ps.Skills.GetValueOrDefault("melee", p.SkillMelee);
        p.SkillBow = ps.Skills.GetValueOrDefault("bow", p.SkillBow);
        p.SkillThrow = ps.Skills.GetValueOrDefault("throw", p.SkillThrow);
        p.SkillSave = ps.Skills.GetValueOrDefault("save", p.SkillSave);
        p.DisarmSkill = ps.Skills.GetValueOrDefault("disarm", p.DisarmSkill);
        p.SkillDevice = ps.Skills.GetValueOrDefault("device", p.SkillDevice);
        foreach (var (id, value) in ps.Timed)
            if (data.Timed(id) is { } def) p.Timed.Set(def, value);
        foreach (var item in ps.Pack.Select(ToItem).OfType<Item>()) p.Inventory.Add(item);
        foreach (var item in ps.Quiver.Select(ToItem).OfType<Item>()) p.Inventory.Add(item);
        for (var slot = 0; slot < ps.Equipment.Count && slot < Inventory.Slots.Count; slot++)
            if (ps.Equipment[slot] is { } e && ToItem(e) is { } worn) p.Inventory.RestoreEquipment(slot, worn);

        // Level (and, mid-duel, the one waiting outside the arena).
        (Level Level, KnownMap Known) ReadLevel(LevelSave ls)
        {
            var level = new Level(data.Terrain, ls.Width, ls.Height, ls.Depth)
            {
                ProfileId = ls.ProfileId, Seed = ls.Seed, IsLit = ls.IsLit, IsKnown = ls.IsKnown,
                Feeling = ls.Feeling, FeelingSquaresSeen = ls.FeelingSquaresSeen,
                Decoy = ls.Decoy is { } decoy ? new Loc(decoy % ls.Width, decoy / ls.Width) : null,
                Population = ls.Population.Length == 4 ? new PopulationBudget(ls.Population[0], ls.Population[1], ls.Population[2], ls.Population[3]) : default,
            };
            foreach (var i in ls.FeelSquares.Where(i => i >= 0 && i < ls.Width * ls.Height))
                level.FeelSquares.Add(new Loc(i % ls.Width, i / ls.Width));
            var featureMap = ls.Terrain.Select(id =>
            {
                if (data.Terrain.TryGet(id, out var t)) return t.Index;
                errors.Add($"unknown terrain '{id}'");
                return (ushort)0;
            }).ToArray();
            var known = new KnownMap(ls.Width, ls.Height);
            var squares = level.Squares;
            if (ls.Features.Length != squares.Length) errors.Add("level size does not match its squares");
            for (var i = 0; i < squares.Length && i < ls.Features.Length; i++)
            {
                squares[i].Feature = featureMap.ElementAtOrDefault(ls.Features[i]);
                squares[i].Flags = (SquareFlags)ls.Flags[i];
                if (ls.Traps.TryGetValue(i, out var trapId))
                    squares[i].Trap = data.Traps.FirstOrDefault(t => t.Id == trapId)?.Index ?? 0;
                if (ls.Locks.TryGetValue(i, out var lockPower)) squares[i].LockPower = (byte)lockPower;
                if (ls.Known[i] >= 0) known.RestoreFeature(i, featureMap.ElementAtOrDefault(ls.Known[i]));
            }
            foreach (var (index, saved) in ls.RememberedObjects)
                if (ToItem(saved) is { } item) known.RememberObject(new Loc(index % ls.Width, index / ls.Width), item);

            foreach (var ms in ls.Monsters)
            {
                if (data.Monster(ms.Race) is not { } race)
                {
                    errors.Add($"unknown monster '{ms.Race}'");
                    continue;
                }
                var m = new Monster(ms.Id, race, new Loc(ms.X, ms.Y), ms.MaxHp, ms.Speed)
                {
                    Hp = ms.Hp, Energy = ms.Energy, Sleep = ms.Sleep, Stun = ms.Stun, Confused = ms.Confused, Fear = ms.Fear,
                    Held = ms.Held, WanderStuck = ms.WanderStuck, Fast = ms.Fast, Slow = ms.Slow,
                    Camouflaged = ms.Camouflaged, BodyguardOf = ms.BodyguardOf, OriginalRace = ms.OriginalRace is null ? null : data.Monster(ms.OriginalRace), MimicItem = ms.MimicItem is null ? null : ToItem(ms.MimicItem),
                    WanderTarget = ms.WanderTarget is [var wx, var wy] ? new Loc(wx, wy) : null,
                };
                m.Carried.AddRange(ms.Carried.Select(ToItem).OfType<Item>());
                m.LootRolled = ms.LootRolled;
                level.Monsters.Restore(m);
            }
            level.Monsters.ReserveUpTo(ls.NextMonsterId);
            foreach (var pile in ls.Objects)
            foreach (var item in pile.Items.Select(ToItem).OfType<Item>())
                level.Objects.Add(new Loc(pile.X, pile.Y), item);
            return (level, known);
        }

        var ls = f.Level;
        var (level, known) = ReadLevel(ls);
        var squares = level.Squares;
        g.RestoreLevel(level, known);
        if (ls.Scent.Length == squares.Length) g.Scent.Restore(ls.Width, ls.Scent, ls.ScentNow);
        else g.Scent.Reset(level);
        if (f.ArenaReturn is { } outside)
        {
            var (backLevel, backKnown) = ReadLevel(outside);
            g.RestoreArena(backLevel, backKnown, new Loc(f.ArenaReturnX, f.ArenaReturnY));
        }
        if (f.CommandedMonster > 0) g.RestoreCommanded(f.CommandedMonster);
        g.StoreDays = f.StoreDays;

        // Stores.
        foreach (var ss in f.Stores)
        {
            if (data.Store(ss.Id) is not { } def || data.Shops.FirstOrDefault(s => s.Id == ss.Id) is not { } shop) continue;
            var store = new Store(def, shop, def.Owners.FirstOrDefault(o => o.Name == ss.Owner) ?? def.Owners.FirstOrDefault());
            store.Stock.AddRange(ss.Stock.Select(ToItem).OfType<Item>());
            g.RestoreStore(store);
        }

        if (errors.Count > 0)
            throw new SaveGameException("The save refers to game data that no longer exists: " + string.Join(", ", errors.Distinct()) + ".");

        // Scheduler: the player, the monsters, the game turn and whoever was still due to act.
        g.Scheduler.Clear();
        g.Scheduler.Add(p);
        foreach (var m in level.Monsters.All) g.Scheduler.Add(m);
        g.Scheduler.SetGameTurn(f.GameTurn);
        g.Scheduler.SetPending(f.PendingActors
            .Select(id => id == Player.PlayerActorId ? (IActor)p : level.Monsters[id])
            .OfType<IActor>());

        g.RecalculateBonuses();
        g.UpdateView();
        return g;
    }
}
