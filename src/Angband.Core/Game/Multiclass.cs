using Angband.Core.Definitions;

namespace Angband.Core.Game;

/// <summary>
/// AVABand's multiclasses: a Warrior who also casts the spells of a Mage, Priest, Druid or Necromancer
/// (Warrior/Mage...). Made from the two classes as one class, so the rest of the game treats it as any
/// other: skills, hit die, stat bonuses and blows halfway between the two; the caster's realm, books,
/// spells, armour-weight limit and passive abilities, and the warrior's (all but the caster's zero-fail);
/// each spell needed at half as many levels again (a level-10 spell at 15, never past 50), cast as at
/// two-thirds of your level (its power and your mana); the warrior's kit (without its torch, for a
/// Necromancer's pair) and the caster's first book;
/// and half as much experience again for every level.
/// </summary>
public static class Multiclass
{
    /// <summary>The fighter of each pair.</summary>
    public const string Fighter = "warrior";

    /// <summary>The casters a warrior can pair with.</summary>
    public static readonly string[] Casters = ["mage", "priest", "druid", "necromancer"];

    /// <summary>Experience needed for each level, beyond the race's: half as much again.</summary>
    public const int ExpPenalty = 50;

    public const int SpellLevelPercent = 150;
    public const int CasterLevelPercent = 67;

    /// <summary>Every pair the data allows (a class missing from the data makes none); <paramref name="isBook"/>: whether a kind is a spellbook.</summary>
    public static IReadOnlyList<ClassDef> All(IReadOnlyList<ClassDef> classes, Func<string, bool> isBook) =>
        classes.FirstOrDefault(c => c.Id == Fighter) is not { } fighter ? []
            : [.. Casters.Select(id => classes.FirstOrDefault(c => c.Id == id)).OfType<ClassDef>().Select(caster => Merge(fighter, caster, isBook))];

    public static ClassDef Merge(ClassDef fighter, ClassDef caster, Func<string, bool> isBook)
    {
        static Dictionary<string, int> Average(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b) =>
            a.Keys.Union(b.Keys).ToDictionary(k => k, k => (int)Math.Round((a.GetValueOrDefault(k) + b.GetValueOrDefault(k)) / 2.0,
                MidpointRounding.AwayFromZero));
        static int Half(int a, int b) => (a + b + 1) / 2;
        return new ClassDef
        {
            Id = $"{fighter.Id}_{caster.Id}",
            Name = $"{fighter.Name}/{caster.Name}",
            Titles = fighter.Titles,
            HitDie = Half(fighter.HitDie, caster.HitDie),
            Skills = Average(fighter.Skills, caster.Skills),
            SkillsPer10Levels = Average(fighter.SkillsPer10Levels, caster.SkillsPer10Levels),
            Stats = Average(fighter.Stats, caster.Stats),
            Blows = Half(fighter.Blows, caster.Blows),
            ExpFactor = Math.Max(fighter.ExpFactor, caster.ExpFactor) + ExpPenalty,
            Realm = caster.Realm,
            MaxAttacks = Half(fighter.MaxAttacks, caster.MaxAttacks),
            MinWeight = Half(fighter.MinWeight, caster.MinWeight),
            StrengthMultiplier = Half(fighter.StrengthMultiplier, caster.StrengthMultiplier),
            SpellWeight = caster.SpellWeight,
            FirstSpellLevel = Math.Max(1, caster.FirstSpellLevel * SpellLevelPercent / 100),
            // (A necromancer's half keeps to the dark: no torch from the warrior's kit, as the necromancer starts without.)
            StartingKit = [.. fighter.StartingKit.Where(k => !caster.Flags.Contains(ClassFlags.Unlight) || k.Kind != "wooden_torch"),
                           .. caster.StartingKit.Where(k => isBook(k.Kind))],
            Flags = [.. fighter.Flags.Union(caster.Flags).Where(f => f != "ZERO_FAIL")],
            Description = $"A {fighter.Name.ToLowerInvariant()} who has also learned the {caster.Name.ToLowerInvariant()}'s magic: "
                          + "skills and toughness halfway between the two, the caster's spells (each a little later than a "
                          + $"{caster.Name.ToLowerInvariant()} learns it, and a little weaker), the warrior's way with weapons — "
                          + "and half as much experience again needed for every level.",
            Components = [fighter.Id, caster.Id],
            SpellClass = caster.Id,
            SpellLevelPercent = SpellLevelPercent,
            CasterLevelPercent = CasterLevelPercent,
        };
    }

    /// <summary>A caster's spell as a multiclass has it: needed later (never past 50), all else the same.</summary>
    public static ClassSpellInfo Adjust(ClassSpellInfo info, ClassDef cls) =>
        cls.SpellLevelPercent == 100 ? info
            : new ClassSpellInfo { Level = Math.Min(50, Math.Max(1, info.Level * cls.SpellLevelPercent / 100)), Mana = info.Mana, Fail = info.Fail, Exp = info.Exp };
}
