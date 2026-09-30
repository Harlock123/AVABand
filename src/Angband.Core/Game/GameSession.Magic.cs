using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Magic;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>Classes, experience levels, spell points, studying and casting spells.</summary>
public sealed partial class GameSession
{
    /// <summary>Angband PY_REGEN_MNBASE: base mana regeneration in 1/65536 points.</summary>
    private const int ManaRegenBase = 524;

    // --- Class and levels -------------------------------------------------------------------------

    /// <summary>
    /// Creates the character (Angband player_birth): stats from the spec plus race and class
    /// adjustments, skills, hit points, racial abilities, starting gold and the class kit.
    /// </summary>
    /// <summary>
    /// Angband get_ahw and get_history: age, height and weight, and a background read from the race's
    /// history charts. (Rolled on their own stream of the seed, so the game's own dice fall as before.)
    /// </summary>
    private void RollBackground(RaceDef race)
    {
        var rng = new GameRandom(GameRandom.DeriveSeed(Seed, 0xB10B));
        Player.Age = race.Age.Base + rng.RandInt1(race.Age.Mod);
        Player.Height = rng.Normal(race.Height.Base, race.Height.Mod);
        Player.Weight = rng.Normal(race.Weight.Base, race.Weight.Mod);
        Player.Background = BackgroundFrom(Data, race.History, rng);
    }

    /// <summary>Angband get_history: from the chart, the first entry whose roll reaches 1d100, then its next chart.</summary>
    public static string BackgroundFrom(GameData data, int chart, GameRandom rng)
    {
        var text = new System.Text.StringBuilder();
        for (var guard = 0; chart != 0 && guard < 100; guard++)
        {
            if (data.Histories.FirstOrDefault(h => h.Chart == chart) is not { } current) break;
            var roll = rng.RandInt1(100);
            if (current.Entries.FirstOrDefault(e => roll <= e.Roll) is not { } entry) break;
            text.Append(entry.Text);
            chart = entry.Next;
        }
        return text.ToString().Trim();
    }

    private void ApplyCharacter(CharacterSpec spec, RaceDef? race, ClassDef cls)
    {
        var p = Player;
        p.Name = spec.Name;
        p.Race = race;
        p.Class = cls;
        p.HeroicBirth = spec.Method.IsHeroic();
        foreach (var stat in CharacterSpec.StatIds)
        {
            p.BaseStats[stat] = spec.BaseStats.GetValueOrDefault(stat, Data.Constants.BaseStat);
            p.NaturalStats[stat] = p.Stats[stat] = Birth.FinalStat(p.BaseStats[stat], race, cls, stat);
        }
        p.BaseBlows = cls.Blows;
        p.HitDie = (race?.HitDie ?? Data.Constants.RaceHitDie) + cls.HitDie;
        p.ExpFactor = (race?.ExpFactor ?? 100) + cls.ExpFactor;
        p.Infravision = race?.Infravision ?? 0;
        p.Regenerates = race?.Flags.Contains("REGENERATE") == true;
        p.IntrinsicResists.Clear();
        foreach (var r in race?.Resists ?? []) p.IntrinsicResists[r] = 1;

        // Level 1 hit points: the full hit die plus Constitution's share.
        p.MaxHp = p.Hp = Math.Max(1, p.HitDie + ConstitutionHp());
        p.Food = Data.Constants.FoodFull - 1;
        ApplySkills();

        // Angband player_outfit: the class's kit, paid for out of the starting gold (never below nothing).
        InitItems(generalKit: false);
        var worth = GiveKit(cls.StartingKit, $"Class '{cls.Id}' kit");
        p.Gold = (int)Math.Max(0, Birth.StartingGold(spec, Data.Constants) - worth);

        // Necromancers shun the light: the torch everyone starts with goes in the pack.
        if (cls.Flags.Contains(ClassFlags.Unlight) && Player.Inventory.Equipped.FirstOrDefault(i => i.Base.Slot == EquipSlot.Light) is { } light)
            Player.Inventory.TakeOff(light);

        RecalculateBonuses();
        RecalculateMana();
        p.Mana = p.MaxMana;
    }

    /// <summary>Hit points Constitution gives per level (carrying fractions between levels).</summary>
    private int ConstitutionHp()
    {
        var perLevel = StatTables.HitPointsPerLevel[StatTables.Index(Player.Stats.GetValueOrDefault("con", 15))];
        var total = Player.ConHpRemainder + perLevel;
        Player.ConHpRemainder = total % 100;
        return total / 100;
    }

    /// <summary>
    /// The search skill in play (Angband SKILL_SEARCH): class, race and level, plus five for each
    /// point of searching on your gear or shape.
    /// </summary>
    public int SearchSkill =>
        Player.SkillSearch + 5 * (Player.Inventory.Equipped.Sum(i => i.Modifier(ItemModifiers.Searching))
                                  + (PlayerShape?.Modifiers.GetValueOrDefault(ItemModifiers.Searching) ?? 0));

    /// <summary>For saves from before the search skill: work it out from class, race and level.</summary>
    internal void RecomputeSkills() => ApplySkills();

    /// <summary>Class skills at the current level (base + per-10-levels growth).</summary>
    private void ApplySkills()
    {
        if (Player.Class is not { } cls) return;
        var race = Player.Race;
        int Skill(string name, int fallback) =>
            cls.Skills.TryGetValue(name, out var b)
                ? b + (race?.Skills.GetValueOrDefault(name) ?? 0) + cls.SkillsPer10Levels.GetValueOrDefault(name) * Player.Level / 10
                : fallback;
        Player.SkillMelee = Skill("melee", Player.SkillMelee);
        Player.SkillBow = Skill("bow", Player.SkillBow);
        Player.SkillThrow = Skill("throw", Player.SkillThrow);
        Player.SkillSave = Skill("save", Player.SkillSave)
                           + StatTables.SavingThrow[StatTables.Index(Player.Stats.GetValueOrDefault("wis", 15))];
        Player.DisarmSkill = Skill("disarm", Player.DisarmSkill);
        Player.DisarmMagicSkill = Skill("disarm_magic", Player.DisarmMagicSkill);
        Player.SkillSearch = Skill("search", Player.SkillSearch);
        Player.SkillDevice = Skill("device", Player.SkillDevice);
        if (PlayerShape is { } shape)
        {
            Player.SkillMelee += shape.Skills.GetValueOrDefault("melee");
            Player.SkillSave += shape.Skills.GetValueOrDefault("save");
            Player.DisarmSkill += shape.Skills.GetValueOrDefault("disarm");
            Player.DisarmMagicSkill += shape.Skills.GetValueOrDefault("disarm_magic");
            Player.SkillSearch += shape.Skills.GetValueOrDefault("search");
            Player.SkillDevice += shape.Skills.GetValueOrDefault("device");
        }
        Player.BaseStealth = Skill("stealth", Player.BaseStealth);
    }

    /// <summary>Experience needed to reach the level after <paramref name="level"/>.</summary>
    public long ExperienceForLevel(int level) =>
        level >= StatTables.MaxLevel ? long.MaxValue : (long)StatTables.ExperienceForLevel[level - 1] * Player.ExpFactor / 100;

    /// <summary>Adds experience and handles any level gains (Angband player_exp_gain / check_experience).</summary>
    public void GainExperience(long amount)
    {
        if (amount <= 0) return;
        Player.Experience += amount;
        // Angband: while drained, a tenth of what you gain also goes toward your maximum.
        Player.MaxExperience = Player.Experience >= Player.MaxExperience
            ? Player.Experience
            : Player.MaxExperience + amount / 10;
        CheckExperience(allowLoss: false);
    }

    /// <summary>
    /// Angband player_exp_lose: drains experience (optionally the maximum too) and loses levels
    /// that no longer have the experience behind them.
    /// </summary>
    public void LoseExperience(long amount, bool permanent = false)
    {
        if (amount <= 0) return;
        Player.Experience = Math.Max(0, Player.Experience - amount);
        if (permanent) Player.MaxExperience = Player.Experience;
        CheckExperience();
    }

    /// <summary>Restores drained experience (Angband restore life levels).</summary>
    public bool RestoreExperience()
    {
        if (Player.Experience >= Player.MaxExperience) return false;
        Publish(new MessageEvent("You feel your life energies returning."));
        Player.Experience = Player.MaxExperience;
        CheckExperience();
        return true;
    }

    /// <summary>Levels up or down to match the current experience (Angband check_experience).</summary>
    private void CheckExperience(bool allowLoss = true)
    {
        while (allowLoss && Player.Level > 1 && Player.Experience < ExperienceForLevel(Player.Level - 1))
        {
            // Hit points from the lost level go with it, and come back if it is regained.
            var gain = Player.HpGains.ElementAtOrDefault(Player.Level - 2);
            Player.Level--;
            Player.MaxHp = Math.Max(1, Player.MaxHp - gain);
            Player.Hp = Math.Min(Player.Hp, Player.MaxHp);
            Publish(new MessageEvent($"Dropped back to level {Player.Level}."));
            ApplySkills();
            RecalculateBonuses();
            RecalculateMana();
        }
        while (Player.Level < StatTables.MaxLevel && Player.Experience >= ExperienceForLevel(Player.Level))
        {
            Player.Level++;
            int gain;
            if (Player.Level > Player.MaxLevel)
            {
                Player.MaxLevel = Player.Level;
                gain = Math.Max(1, Rng.RandInt1(Player.HitDie) + ConstitutionHp());
                while (Player.HpGains.Count < Player.Level - 1) Player.HpGains.Add(0);
                Player.HpGains[Player.Level - 2] = gain;
            }
            else gain = Player.HpGains.ElementAtOrDefault(Player.Level - 2);
            Player.MaxHp += gain;
            Player.Hp += gain;
            // Angband 4.2: gaining a level restores drained stats.
            foreach (var stat in Player.StatDrain.Keys.ToList()) Player.StatDrain[stat] = 0;
            AddHistory($"Reached level {Player.Level}");
            Publish(new MessageEvent($"Welcome to level {Player.Level}."));
            Publish(new LevelUpEvent(Player.Level));
            ApplySkills();
            RecalculateBonuses();
            RecalculateMana();
            var learnable = StudyableSpells().Count();
            if (learnable > 0)
                Publish(new MessageEvent($"You can learn {learnable} new {RealmNoun()}{(learnable == 1 ? "" : "s")}."));
        }
    }

    // --- Spell points -------------------------------------------------------------------------

    public RealmDef? PlayerRealm => Player.Class?.Realm is { } r ? Data.Realm(r) : null;

    private int CastingStatIndex => StatTables.Index(Player.Stats.GetValueOrDefault(PlayerRealm?.Stat ?? "int", 10));

    /// <summary>Angband calc_mana: 1 point plus a stat-based amount per caster level.</summary>
    public void RecalculateMana()
    {
        var cls = Player.Class;
        if (cls?.Realm is null || Player.Level < cls.FirstSpellLevel)
        {
            Player.MaxMana = Player.Mana = 0;
            return;
        }
        var levels = Player.Level - cls.FirstSpellLevel + 1;
        Player.MaxMana = 1 + StatTables.ManaPerLevel[CastingStatIndex] * levels / 100;
        Player.Mana = Math.Min(Player.Mana, Player.MaxMana);
    }

    /// <summary>Angband player_regen_mana: the same fixed-point trickle as hit points.</summary>
    private void RegenerateMana()
    {
        if (ClassHas(ClassFlags.CombatRegen))
        {
            CombatRegenDecay();
            return;
        }
        if (Player.Mana >= Player.MaxMana)
        {
            Player.ManaFraction = 0;
            return;
        }
        var percent = RegenNormal * (Player.IsResting ? 2 : 1);
        if (Player.HasGearFlag(ItemFlags.ImpairMana)) percent /= 2; // Angband player_regen_mana: IMPAIR_MANA
        var gain = (long)Player.MaxMana * percent + ManaRegenBase;
        Player.Mana += (int)(gain >> 16);
        Player.ManaFraction += (int)(gain & 0xFFFF);
        if (Player.ManaFraction >= 0x10000)
        {
            Player.ManaFraction -= 0x10000;
            Player.Mana++;
        }
        if (Player.Mana >= Player.MaxMana)
        {
            Player.Mana = Player.MaxMana;
            Player.ManaFraction = 0;
        }
    }

    // --- Spells ---------------------------------------------------------------------------------

    private string RealmNoun() => PlayerRealm?.SpellNoun ?? "spell";

    /// <summary>The player's class data for a spell, or null if the class can't use it.</summary>
    public ClassSpellInfo? SpellInfo(SpellDef spell) =>
        Player.Class is { } cls && spell.Classes.TryGetValue(cls.Id, out var info) ? info : null;

    /// <summary>All spells of the player's class, in book order.</summary>
    public IEnumerable<SpellDef> ClassSpells => Data.Spells.Where(s => SpellInfo(s) is not null);

    public bool HasBookFor(SpellDef spell) => Player.Inventory.Pack.Any(i => i.Kind.Id == spell.Book);

    /// <summary>Angband obj_kind_can_browse: a book of the player's class (one holding spells they could learn).</summary>
    public bool PlayerCanBrowse(ObjectKindDef kind) =>
        Player.Class is { } cls && Data.Spells.Any(s => s.Book == kind.Id && s.Classes.ContainsKey(cls.Id));

    /// <summary>
    /// For a book, whether it's the player's (so a newcomer doesn't buy, or sell, the wrong kind):
    /// "for you", or "not for a Warrior"; null for anything else.
    /// </summary>
    public string? BookNote(Item item)
    {
        if (!ItemNaming.IsBook(item.Base.Id)) return null;
        if (PlayerCanBrowse(item.Kind)) return "for you";
        var cls = Player.Class?.Name ?? "you";
        return $"not for {("AEIOU".Contains(cls[0]) ? "an" : "a")} {cls}";
    }

    /// <summary>Spells the player could learn now (level reached, book carried, not yet learned).</summary>
    public IEnumerable<SpellDef> StudyableSpells() =>
        ClassSpells.Where(s => SpellInfo(s)!.Level <= Player.Level && !Player.LearnedSpells.Contains(s.Id) && HasBookFor(s));

    /// <summary>Angband spell_chance: base fail, less 3 per level above the spell and the stat bonus.</summary>
    public int SpellFailChance(SpellDef spell)
    {
        if (SpellInfo(spell) is not { } info) return 100;
        var idx = CastingStatIndex;
        var chance = info.Fail - 3 * (Player.Level - info.Level) - StatTables.FailReduction[idx];
        if (info.Mana > Player.Mana) chance += 5 * (info.Mana - Player.Mana);
        if (UnlightPenalty) chance += 25;
        // Angband: only ZERO_FAIL classes (mage, priest, druid, necromancer) can get below 5%.
        var minimum = StatTables.MinimumFail[idx];
        if (!ClassHas("ZERO_FAIL")) minimum = Math.Max(5, minimum);
        // Angband spell_chance: fear makes spells harder (before the minimum), and the chance is
        // held between the minimum and 50% before stunning adds to it.
        if (PlayerAfraid) chance += 20;
        chance = Math.Clamp(chance, minimum, Math.Max(minimum, 50));
        var stun = Player.Timed[TimedIds.Stun];
        if (stun > 50) chance += 25;
        else if (stun > 0) chance += 15;
        // Angband: amnesia makes spells very difficult.
        if (Player.Timed.Has(TimedIds.Amnesia)) chance = 50 + chance / 2;
        return Math.Clamp(chance, 0, 95);
    }

    /// <summary>Whether the class chooses the spells it learns (Angband CHOOSE_SPELLS); priests and paladins don't.</summary>
    public bool ChoosesSpells => ClassHas(ClassFlags.ChooseSpells);

    /// <summary>The books (object kinds) holding a spell the player could learn now.</summary>
    public IReadOnlyList<string> StudyableBooks() => [.. StudyableSpells().Select(s => s.Book).Distinct()];

    private int Study(string? spellId, string? book = null)
    {
        if (PlayerRealm is null)
        {
            Publish(new MessageEvent("You cannot learn any spells!"));
            return 0;
        }
        if (Player.IsBlind || Player.Timed.Has(TimedIds.Confused))
        {
            Publish(new MessageEvent(Player.IsBlind ? "You cannot see!" : "You are too confused!"));
            return 0;
        }

        var options = StudyableSpells().ToList();
        SpellDef? spell;
        if (spellId is not null) spell = options.FirstOrDefault(s => s.Id == spellId);
        else if (!ChoosesSpells || book is not null)
        {
            // Angband do_cmd_study_book: a spell of the book, each learnable one equally likely.
            spell = null;
            var seen = 0;
            foreach (var s in options.Where(s => s.Book == (book ?? options.FirstOrDefault()?.Book)))
                if (++seen == 1 || Rng.RandInt0(seen) == 0) spell = s;
        }
        else spell = options.FirstOrDefault();
        if (spell is null)
        {
            Publish(new MessageEvent(book is not null && spellId is null
                ? $"You cannot learn any {RealmNoun()}s in that book."
                : $"You cannot learn any new {RealmNoun()}s."));
            return 0;
        }

        Player.LearnedSpells.Add(spell.Id);
        Publish(new MessageEvent($"You have learned the {RealmNoun()} of {spell.Name}."));
        Publish(new SpellLearnedEvent(spell.Id));
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// Casts a learned spell (Angband spell_cast). Casting without enough mana is only attempted when
    /// <paramref name="allowOverexert"/> is set, and then costs hit points' worth of fainting.
    /// </summary>
    private int Cast(string spellId, Loc? target, Direction? direction, bool allowOverexert)
    {
        var spell = Data.Spell(spellId);
        if (spell is null || SpellInfo(spell) is not { } info || !Player.LearnedSpells.Contains(spellId))
        {
            Publish(new MessageEvent($"You don't know that {RealmNoun()}."));
            return 0;
        }
        if (!HasBookFor(spell))
        {
            Publish(new MessageEvent("You need the book to cast from."));
            return 0;
        }
        if (Player.IsBlind || Player.Timed.Has(TimedIds.Confused))
        {
            Publish(new MessageEvent(Player.IsBlind ? "You cannot see!" : "You are too confused!"));
            return 0;
        }
        if (info.Mana > Player.Mana && !allowOverexert)
        {
            Publish(new MessageEvent($"You do not have enough mana to {PlayerRealm!.Verb} this {RealmNoun()}."));
            return 0;
        }
        if (spell.NeedsDirection && direction is null)
        {
            Publish(new MessageEvent("You must choose a direction."));
            return 0;
        }
        if (NeedsCurseChoice(spell.Effect) && UncursableItems().Count == 0)
        {
            Publish(new MessageEvent("You have no curses to remove."));
            return 0;
        }
        if (SpellNeedsTarget(spell) && target is null && AimPoint() is null)
        {
            Publish(new MessageEvent("You have no target."));
            return 0;
        }

        var castFast = false;
        if (Rng.RandInt0(100) < SpellFailChance(spell))
        {
            Publish(new MessageEvent("You failed to concentrate hard enough!"));
        }
        else
        {
            Publish(new SpellCastEvent(spell.Id, spell.Realm));
            ApplySpellEffects(spell, target, direction);
            RecalculateBonuses();
            // Angband: casting heals a blackguard a little, by the mana spent.
            if (ClassHas(ClassFlags.CombatRegen)) ConvertManaToHp((long)info.Mana << 16);
            castFast = Player.Timed.Has("fastcast");
            if (Player.CastSpells.Add(spell.Id))
            {
                GainExperience(info.Exp * info.Level);
            }
        }

        // Pay for it, fainting if the caster overreached (Angband: paralysis for the shortfall).
        if (info.Mana <= Player.Mana) Player.Mana -= info.Mana;
        else
        {
            var oops = info.Mana - Player.Mana;
            Player.Mana = 0;
            Player.ManaFraction = 0;
            Publish(new MessageEvent("You faint from the effort!"));
            IncreaseTimed(TimedIds.Paralyzed, Rng.RandInt1(5 * oops + 1));
        }
        return castFast ? EnergyTable.MoveEnergy * 3 / 4 : EnergyTable.MoveEnergy; // Angband: Mana Channel speeds casting
    }

    /// <summary>Whether a spell must be aimed at a monster (bolts, balls, leaps...), rather than a direction.</summary>
    public static bool SpellNeedsTarget(SpellDef spell) =>
        !spell.NeedsDirection && (NeedsAim(spell.Effect) || ItemEffects.Parse(spell.Effect).Any(e => AimedSpellEffects.Contains(e.Name)));

    private static readonly HashSet<string> AimedSpellEffects =
        ["leap", "bolt_or_beam", "short_beam", "arc", "strike", "swarm", "teleport_to", "earthquake_at"];

    /// <summary>Runs a spell's effect list, evaluating level expressions in its arguments.</summary>
    private void ApplySpellEffects(SpellDef spell, Loc? target, Direction? direction)
    {
        _effectTarget = target;
        _effectDirection = direction;
        _effectSource = spell.Name;
        try
        {
            ApplySpellEffectList(spell, SpellEffects.Parse(spell.Effect, Player.Level).ToList(), target, direction);
        }
        finally
        {
            _effectTarget = null;
            _effectDirection = null;
            _effectSource = "";
        }
    }

    private void ApplySpellEffectList(SpellDef spell, List<ItemEffect> effects, Loc? target, Direction? direction)
    {
        for (var i = 0; i < effects.Count && !Player.IsDead; i++)
        {
            var effect = effects[i];
            if (effect.Name == "random" && effect.Int(0) > 0)
            {
                // One of the next N effects, chosen at random (Unleash Chaos).
                var n = Math.Min(effect.Int(0), effects.Count - i - 1);
                if (n > 0) ApplySpellEffectList(spell, [effects[i + 1 + Rng.RandInt0(n)]], target, direction);
                i += n;
                continue;
            }
            if (ApplyClassSpellEffect(effect)) continue;
            switch (effect.Name)
            {
                case "bolt" when effect.Arg(1).Contains('d'):
                case "ball" when effect.Arg(1).Contains('d'):
                    // Dice-valued bolts and balls work just as a wand's do.
                    ApplyEffects(effect.ToEffectString());
                    break;
                case "bolt":
                {
                    var dice = new Randomness.Dice(effect.Int(1), effect.Int(2), effect.Int(3));
                    SpellBolt(spell, NullIfNone(effect.Arg(0)), dice.Roll(Rng), target);
                    break;
                }
                case "ball":
                    SpellBall(spell, NullIfNone(effect.Arg(0)), effect.Int(1), effect.Int(2), target);
                    break;
                case "stone_to_mud":
                    StoneToMud(direction ?? Direction.Here, effect.Int(0) > 0 ? effect.Int(0) : MaxRange);
                    break;
                case "detect_evil":
                    DetectMonsters(effect.Int(0), "EVIL");
                    break;
                case "light_damage":
                    LightDamage(new Randomness.Dice(effect.Int(0), effect.Int(1)));
                    break;
                default:
                    ApplyEffects(effect.ToEffectString());
                    break;
            }
        }
    }

    private static string? NullIfNone(string element) => element is "" or "none" ? null : element;

    /// <summary>Aims at an explicit target or the nearest visible monster.</summary>
    private Loc? SpellTarget(Loc? target)
    {
        if (target is not null) return target;
        if (AimPoint() is { } aim) return aim;
        Publish(new MessageEvent("You have no target."));
        return null;
    }

    /// <summary>A bolt stops at the first monster in its path.</summary>
    private void SpellBolt(SpellDef spell, string? element, int damage, Loc? target) =>
        ProjectBolt(spell.Name, element, damage, target);

    private void SpellBall(SpellDef spell, string? element, int damage, int radius, Loc? target) =>
        ProjectBall(spell.Name, element, damage, radius, target);

    /// <summary>A bolt from the player: hits the first monster in its path.</summary>
    private void ProjectBolt(string name, string? element, int damage, Loc? target) =>
        ProjectBoltEffect(name, target, m => ProjectileHitsMonster(m, name, element, damage), element);

    /// <summary>Fires a bolt and applies <paramref name="hit"/> to the first monster it meets. False if it hit nothing.</summary>
    private bool ProjectBoltEffect(string name, Loc? target, Action<Monster> hit, string? element = null)
    {
        if (SpellTarget(target) is not { } aim) return false;
        var path = ProjectionPath.Compute(Level, Player.Position, aim, MaxRange, PathFlags.StopAtCreature,
            p => Level.Monsters.At(p) is not null);
        Publish(new MissileFiredEvent(name, Player.Position, path));
        ShowProjection(Player.Position, path, null, element, ProjectionKind.Bolt);
        if (path.Count > 0 && Level.Monsters.At(path[^1]) is { } monster)
        {
            TrackHealth(monster); // a bolt affects it and nobody else
            hit(monster);
            return true;
        }
        return false;
    }

    /// <summary>A beam: affects every monster along its path.</summary>
    private void ProjectBeam(string name, string? element, int damage, Loc? target)
    {
        if (SpellTarget(target) is not { } aim) return;
        var path = ProjectionPath.Compute(Level, Player.Position, aim, MaxRange, PathFlags.Through);
        Publish(new MissileFiredEvent(name, Player.Position, path));
        ShowProjection(Player.Position, path, null, element, ProjectionKind.Beam);
        foreach (var p in path)
        {
            if (!Level.Has(p, TerrainFlags.Project)) break;
            if (Level.Monsters.At(p) is { } monster) ProjectileHitsMonster(monster, name, element, damage);
        }
    }

    /// <summary>A ball explodes at the first monster (or the target); damage falls off with distance.</summary>
    private void ProjectBall(string name, string? element, int damage, int radius, Loc? target)
    {
        if (SpellTarget(target) is not { } aim) return;
        var path = ProjectionPath.Compute(Level, Player.Position, aim, MaxRange, PathFlags.StopAtCreature,
            p => Level.Monsters.At(p) is not null);
        var center = path.Count == 0 ? Player.Position : path[^1];
        if (!Level.Has(center, TerrainFlags.Project) && path.Count > 1) center = path[^2];
        Publish(new MissileFiredEvent(name, Player.Position, path));
        ShowProjection(Player.Position, path, BallArea(center, radius), element, ProjectionKind.Ball);

        foreach (var monster in Level.Monsters.All
                     .Where(m => m.Position.DistanceTo(center) <= radius && ProjectionPath.Projectable(Level, center, m.Position, radius + 1))
                     .OrderBy(m => m.Position.DistanceTo(center)).ThenBy(m => m.Id).ToList())
            ProjectileHitsMonster(monster, name, element, BallDamage(damage, monster.Position.DistanceTo(center)), monster.Position.DistanceTo(center));
        DestroyFloorObjects(BallArea(center, radius), element);
    }

    private void SpellHitsMonster(Monster monster, SpellDef spell, string? element, int damage) =>
        ProjectileHitsMonster(monster, spell.Name, element, damage);


    /// <summary>Light-sensitive monsters in view take damage (Call Light and friends).</summary>
    private void LightDamage(Randomness.Dice dice)
    {
        foreach (var m in Level.Monsters.All.Where(m => Level[m.Position].Has(World.SquareFlags.View)).ToList())
        {
            LearnMonsterResponse(m, "HURT_LIGHT"); // cringing or not, you see how it takes the light
            if (!m.Race.Has("HURT_LIGHT")) continue;
            if (m.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(m))} cringes from the light!"));
            DamageMonster(m, dice.Roll(Rng), pain: true);
        }
    }

    /// <summary>Turns the first diggable wall in a direction to mud (Angband STONE_TO_MUD).</summary>
    private void StoneToMud(Direction dir, int range = MaxRange)
    {
        var p = Player.Position;
        for (var i = 0; i < range; i++)
        {
            p = p.Step(dir);
            if (!Level.InBounds(p)) return;
            if (Level.Monsters.At(p) is { } tested) LearnMonsterResponse(tested, MonsterFlags.HurtRock);
            if (Level.Monsters.At(p) is { } creature && creature.Race.Has(MonsterFlags.HurtRock))
            {
                // Angband: creatures of rock are dissolved by it (20 + d30).
                if (creature.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(creature))} loses some skin!"));
                DamageMonster(creature, 20 + Rng.RandInt1(30));
                return;
            }
            if (Level.IsPassable(p)) continue;

            var feature = Level.FeatureAt(p);
            if (feature.Has(TerrainFlags.Permanent) || !Level.InBoundsFully(p))
            {
                Publish(new MessageEvent("The wall resists."));
                return;
            }
            if (!feature.HasAny(TerrainFlags.Rock | TerrainFlags.DoorAny)) return;

            var treasure = feature.Has(TerrainFlags.Gold);
            ref var sq = ref Level[p];
            sq.Feature = Data.Terrain.Ids.Floor;
            sq.Flags &= ~World.SquareFlags.AnyWallMarker;
            sq.LockPower = 0;
            Publish(new MessageEvent("The wall turns into mud!"));
            if (treasure)
            {
                Level.Objects.Add(p, Objects.MakeGold(Rng, Level.Depth));
                Publish(new MessageEvent("You have found something!"));
            }
            UpdateView();
            return;
        }
    }
}

/// <summary>Parses spell effect strings, evaluating <c>L</c> (caster level) expressions in arguments.</summary>
public static class SpellEffects
{
    /// <summary>Effects understood only by spells (the rest are item effects).</summary>
    public static readonly IReadOnlySet<string> SpellOnly = new HashSet<string>
    {
        "bolt", "ball", "stone_to_mud", "detect_evil", "light_damage",
        // The necromancer's and blackguard's rituals and the rogue's tricks (GameSession.ClassMagic.cs).
        "darken_area", "darken_level", "detect_minds", "detect_fearful", "detect_stairs", "tap_unlife", "crush",
        "vampire_strike", "sweep", "leap", "blows",
        // The later rituals and prayers (GameSession.ArenaAndCommand.cs).
        "gain_mana", "spot", "curse", "command", "light_level", "single_combat",
        // The mage, priest, druid and ranger books (GameSession.RealmMagic.cs).
        "bolt_or_beam", "short_beam", "arc", "strike", "swarm", "sphere", "detect_traps", "detect_living", "destroy_traps",
        "make_doors", "tap_device", "teleport_to", "hold_adjacent", "earthquake_at", "nourish_to", "create_arrows", "brand_ammo", "decoy",
    };

    public static IEnumerable<ItemEffect> Parse(string effects, int level) =>
        ItemEffects.Parse(effects).Select(e => e with { Args = e.Args.Select(a => Evaluate(a, level)).ToList() });

    /// <summary>
    /// Pure arithmetic arguments are evaluated; element names and dice stay as they are. Braces
    /// evaluate a part of an argument, so dice can grow with level: <c>{L/4+3}d4</c>, <c>{L*3}+1d25</c>.
    /// </summary>
    private static string Evaluate(string arg, int level)
    {
        if (arg.Contains('{'))
            return System.Text.RegularExpressions.Regex.Replace(arg, @"\{([^{}]*)\}",
                m => SpellExpr.Eval(m.Groups[1].Value, level).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (arg.Length == 0 || arg.Contains('d') || !arg.Any(c => char.IsDigit(c) || c is 'L' or 'l')) return arg;
        if (arg.Any(char.IsLetter) && !arg.All(c => char.IsDigit(c) || "Ll+-*/() ".Contains(c))) return arg;
        return SpellExpr.Eval(arg, level).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool IsKnown(string name) => SpellOnly.Contains(name) || ItemEffects.Known.Contains(name);
}
