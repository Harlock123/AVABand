using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// AVABand's racial abilities (ava_races.json; the birth option "AVABand's racial abilities",
// birth_ava_races, on by default, turns them off for Angband 4.2.5's races as they are). Each is a
// small rule hooked into the system it touches; this file holds them together:
//  - Human, DUAL_WIELD: a light weapon in the off hand (the shield's place) for an extra blow (GameSession.DualWield.cs).
//  - Half-Elf, TRUSTED: 10% better prices; KEEN_EYE: putting something on reveals one unknown rune.
//  - Elf, ELVEN_ARCHERY: +15 to hit with bows.
//  - Hobbit, STONE_THROWER: +20 to hit with slings and throws; SECOND_BREAKFAST: food lasts half as long again.
//  - Gnome, TINKER: +10 device skill; recharging backfires half as often.
//  - Dwarf, DELVER: +20 digging without a pick or shovel; half as much gold again from a vein with one.
//  - Half-Orc, ORC_KIN: sleeping orcs slower to wake to you; protection from fear.
//  - Half-Troll, TROLL_RAGE: once a level, below a quarter of your life: berserk and a little healing;
//    RUBBLE_SMASHER: rubble cleared at the first blow.
//  - Dúnadan, FORESIGHT: a level's whole feeling on arrival; hold life.
//  - High-Elf, ELDAR_LIGHT: +1 light radius; undead and demons in your light at -2 speed.
//  - Kobold, TRAP_WISE: +10 searching, +15 disarming; VENOMOUS: bare-handed blows poison (+1d6).
public sealed partial class GameSession
{
    /// <summary>Whether the player's race has this AVABand ability (and the birth option is on).</summary>
    public bool HasRaceAbility(string ability) =>
        Options[OptionIds.AvaRaces] && Player.Race?.AvaAbilities.Contains(ability) == true;

    /// <summary>The level a Half-Troll last raged on (once a level).</summary>
    private World.Level? _ragedOn;

    /// <summary>Skills the racial abilities add (called after the class and race skills are worked out).</summary>
    private void RaceAbilitySkills()
    {
        if (HasRaceAbility("STONE_THROWER")) Player.SkillThrow += 20;
        if (HasRaceAbility("TINKER")) Player.SkillDevice += 10;
        if (HasRaceAbility("TRAP_WISE"))
        {
            Player.SkillSearch += 10;
            Player.DisarmSkill += 15;
            Player.DisarmMagicSkill += 15;
        }
    }

    /// <summary>Resistances and protections the racial abilities give (merged into the player's own).</summary>
    private void RaceAbilityResists(Dictionary<string, int> resists)
    {
        if (HasRaceAbility("ORC_KIN")) resists["fear"] = Math.Max(resists.GetValueOrDefault("fear"), 1);
        if (HasRaceAbility("FORESIGHT")) resists["hold_life"] = Math.Max(resists.GetValueOrDefault("hold_life"), 1);
    }

    /// <summary>Light radius the racial abilities add.</summary>
    private int RaceAbilityLight() => HasRaceAbility("ELDAR_LIGHT") ? 1 : 0;

    /// <summary>The shooting skill with a launcher: the class's and race's, and a racial ability's.</summary>
    public int LauncherSkill(Item launcher) => Player.SkillBow + RaceLauncherSkill(launcher);

    /// <summary>To-hit skill with a launcher: an Elf's bows, a Hobbit's slings.</summary>
    private int RaceLauncherSkill(Item launcher) =>
        (HasRaceAbility("ELVEN_ARCHERY") && launcher.Base.Id == "bow" ? 15 : 0)
        + (HasRaceAbility("STONE_THROWER") && launcher.Base.Id == "sling" ? 20 : 0);

    /// <summary>A Hobbit's second breakfast: digestion two-thirds as fast.</summary>
    private int RaceDigestion(int amount) => HasRaceAbility("SECOND_BREAKFAST") ? Math.Max(1, amount * 2 / 3) : amount;

    /// <summary>A Half-Elf is trusted: buying costs 10% less, selling fetches 10% more.</summary>
    private long RacePrice(long price, bool buying) =>
        !HasRaceAbility("TRUSTED") || price <= 0 ? price : buying ? Math.Max(1, price * 9 / 10) : price * 11 / 10;

    /// <summary>A Half-Elf's keen eye: something put on shows one of its unknown runes (never a curse) at once.</summary>
    private void KeenEye(Item worn)
    {
        if (!HasRaceAbility("KEEN_EYE")) return;
        if (Knowledge.UnknownRunes(worn).FirstOrDefault(r => !r.StartsWith("curse", StringComparison.Ordinal)) is { } rune)
            LearnRune(rune);
    }

    /// <summary>A Gnome's recharge: the backfire chance (one in <paramref name="chance"/>) halved.</summary>
    private int RaceRechargeChance(int chance) => HasRaceAbility("TINKER") ? chance * 2 : chance;

    /// <summary>A Dwarf delves: +20 digging without a pick or shovel.</summary>
    private int RaceDigging() => HasRaceAbility("DELVER") && BestDigger?.Base.Id != "digger" ? 20 : 0;

    /// <summary>A Dwarf with a pick or shovel gets half as much gold again from a vein.</summary>
    private void DelverGold(Item gold)
    {
        if (HasRaceAbility("DELVER") && BestDigger?.Base.Id == "digger") gold.GoldValue = gold.GoldValue * 3 / 2;
    }

    /// <summary>A Half-Troll smashes rubble at the first blow.</summary>
    private bool SmashesRubble => HasRaceAbility("RUBBLE_SMASHER");

    /// <summary>A Half-Orc is kin to orcs: a sleeping orc hears them as though they were five stealthier.</summary>
    private int RaceStealthAgainst(Monster monster) => HasRaceAbility("ORC_KIN") && monster.Race.Has("ORC") ? 5 : 0;

    /// <summary>A Half-Troll's rage: once a level, hurt below a quarter of their life, they go berserk and heal a little.</summary>
    private void TrollRage()
    {
        if (!HasRaceAbility("TROLL_RAGE") || Player.IsDead || Player.Hp * 4 >= Player.MaxHp || ReferenceEquals(_ragedOn, Level)) return;
        _ragedOn = Level;
        Publish(new MessageEvent("A troll's rage takes you! You roar, and the pain falls away."));
        Player.Hp = Math.Min(Player.MaxHp, Player.Hp + Player.MaxHp / 6);
        IncreaseTimed("berserk", 15 + Rng.RandInt1(10), check: false);
    }

    /// <summary>A Dúnadan's foresight: the level's feeling squares count as seen from the start.</summary>
    private void Foresight()
    {
        if (!HasRaceAbility("FORESIGHT") || Level.Depth == 0) return;
        Level.FeelingSquaresSeen = LevelFeelings.FeelingNeed;
        Level.FeelSquares.Clear();
    }

    /// <summary>A High-Elf's light: undead and demons within it are slowed (-2 speed); the rest aren't.</summary>
    private void EldarLight()
    {
        var on = HasRaceAbility("ELDAR_LIGHT") && Player.LightRadius > 0;
        foreach (var m in Level.Monsters.All)
            m.AuraSlow = on && (m.Race.Has("UNDEAD") || m.Race.Has(MonsterFlags.Demon))
                         && m.Position.DistanceTo(Player.Position) <= Player.LightRadius ? 2 : 0;
    }

    /// <summary>A Kobold's venomous bare hands: 1d6 more, unless the monster is immune to poison.</summary>
    private int VenomousPunch(Monster monster) =>
        HasRaceAbility("VENOMOUS") && !monster.Race.Has("IM_POIS") ? Rng.RandInt1(6) : 0;
}
