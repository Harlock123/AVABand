using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Items;
using Angband.Core.Magic;
using Angband.Core.Monsters;
using Angband.Core.World;

namespace Angband.Core.Game;

// Monster abilities beyond plain damage: thieves, drains, disenchantment, invisibility and walls
// (Angband mon-blow-effects.c, monster movement flags), and the player's stat and life drains.
public sealed partial class GameSession
{
    /// <summary>Angband adj_dex_safe: protection from thieves by Dexterity.</summary>
    private static readonly int[] AdjDexSafe =
        [0, 1, 2, 3, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 15, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 100, 100, 100, 100, 100, 100, 100];

    /// <summary>Whether the player sees invisible things (gear, race or a timed effect).</summary>
    public bool SeesInvisible => Player.Resists.GetValueOrDefault("see_invis") > 0 || Player.Timed.Has("see_invisible");

    /// <summary>
    /// Whether the player can see a monster: on a seen square (or by infravision for warm-blooded ones),
    /// and not invisible unless the player sees invisible.
    /// </summary>
    public bool MonsterVisible(Monster monster)
    {
        if (monster.Camouflaged) return false;
        if (Senses(monster)) return true;
        if (monster.Race.Has(MonsterFlags.Invisible) && !SeesInvisible) return false;
        var sq = Level[monster.Position];
        return sq.Has(SquareFlags.Seen)
               || (Player.TotalInfravision > 0 && !Player.IsBlind && sq.Has(SquareFlags.View)
                   && !monster.Race.Has("COLD_BLOOD")
                   && monster.Position.DistanceTo(Player.Position) <= Player.TotalInfravision);
    }

    /// <summary>
    /// Telepathy (Angband ESP): monsters with minds nearby are sensed even through walls; mindless
    /// ones never are, and weird minds only flicker into view.
    /// </summary>
    private bool Senses(Monster monster)
    {
        if (!Player.HasGearFlag(ItemFlags.Telepathy) && !Player.Timed.Has("telepathy")) return false;
        if (monster.Race.Has("EMPTY_MIND")) return false;
        if (monster.Race.Has("WEIRD_MIND") && (monster.Id + (int)(GameTurn / 10)) % 10 != 0) return false;
        return monster.Position.DistanceTo(Player.Position) <= Data.Constants.MaxSight * 2;
    }

    // --- Stats -------------------------------------------------------------------------------

    /// <summary>Drains a stat by one point unless sustained (Angband player_stat_dec).</summary>
    public bool DrainStat(string stat)
    {
        if (Player.Resists.GetValueOrDefault("sust_" + stat) > 0)
        {
            Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: false)} for a moment, but the feeling passes."));
            return false;
        }
        var current = Player.Stats.GetValueOrDefault(stat, 15);
        if (current <= 3) return false;
        Player.StatDrain[stat] = Player.StatDrain.GetValueOrDefault(stat) + 1;
        Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: false)}."));
        RecalculateAfterStatChange();
        return true;
    }

    /// <summary>Restores a drained stat (Angband player_stat_res).</summary>
    public bool RestoreStat(string stat)
    {
        if (Player.StatDrain.GetValueOrDefault(stat) <= 0) return false;
        Player.StatDrain[stat] = 0;
        Publish(new MessageEvent($"You feel less {StatAdjective(stat, good: false)}."));
        RecalculateAfterStatChange();
        return true;
    }

    /// <summary>
    /// A permanent stat gain (Angband player_stat_inc, as from potions of Strength): one point, up to
    /// 18/100, also restoring any drain.
    /// </summary>
    public bool GainStat(string stat)
    {
        Player.StatDrain[stat] = 0;
        var natural = Player.NaturalStats.GetValueOrDefault(stat, 15);
        if (natural < 28) Player.NaturalStats[stat] = natural + 1;
        Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: true)}!"));
        RecalculateAfterStatChange();
        return true;
    }

    private void RecalculateAfterStatChange()
    {
        RecalculateBonuses();
        ApplySkills();
        RecalculateMana();
    }

    private static string StatAdjective(string stat, bool good) => (stat, good) switch
    {
        ("str", true) => "strong", ("str", false) => "weak",
        ("int", true) => "smart", ("int", false) => "stupid",
        ("wis", true) => "wise", ("wis", false) => "naive",
        ("dex", true) => "dextrous", ("dex", false) => "clumsy",
        ("con", true) => "healthy", ("con", false) => "sickly",
        _ => good ? "good" : "bad",
    };

    // --- Blow effects ------------------------------------------------------------------------

    /// <summary>
    /// Special blow effects after the damage (Angband monster_blow_effect). Returns true when the
    /// monster has taken what it wanted and teleports away (ending its attack).
    /// </summary>
    private bool ApplyBlowSpecial(Monster monster, string effect, int damage)
    {
        switch (effect)
        {
            case "eat_gold":
                return StealGold(monster);
            case "eat_item":
                return StealItem(monster);
            case "eat_food":
                EatFood();
                return false;
            case "eat_light":
                EatLight();
                return false;
            case "lose_str" or "lose_int" or "lose_wis" or "lose_dex" or "lose_con":
                DrainStat(effect[5..]);
                return false;
            case "lose_all":
                foreach (var stat in CharacterSpec.StatIds) DrainStat(stat);
                return false;
            case "exp_10":
                DrainLife(10, 2);
                return false;
            case "exp_20":
                DrainLife(20, 3);
                return false;
            case "exp_40":
                DrainLife(40, 4);
                return false;
            case "exp_80":
                DrainLife(80, 5);
                return false;
            case "disenchant":
                Disenchant();
                return false;
            case "drain_charges":
                DrainCharges(monster);
                return false;
            default:
                return false;
        }
    }

    /// <summary>The thief's saving throw: Dexterity and level (not while paralysed).</summary>
    private bool ProtectsBelongings() =>
        !Player.Timed.Has(TimedIds.Paralyzed)
        && Rng.RandInt0(100) < AdjDexSafe[StatTables.Index(Player.Stats.GetValueOrDefault("dex", 15))] + Player.Level;

    private bool StealGold(Monster monster)
    {
        if (ProtectsBelongings())
        {
            Publish(new MessageEvent("You quickly protect your money pouch!"));
            return Rng.RandInt0(3) != 0;
        }
        var gold = Player.Gold / 10 + Rng.RandInt1(25);
        if (gold < 2) gold = 2;
        if (gold > 5000) gold = Player.Gold / 20 + Rng.RandInt1(3000);
        gold = Math.Min(gold, Player.Gold);
        if (gold <= 0)
        {
            Publish(new MessageEvent("Nothing was stolen."));
            return true;
        }
        Player.Gold -= gold;
        Publish(new MessageEvent("Your purse feels lighter."));
        Publish(new MessageEvent(Player.Gold <= 0 ? "All of your coins were stolen!" : $"{gold} coins were stolen!"));
        var coins = Objects.MakeGold(Rng, Level.Depth);
        coins.GoldValue = (int)Math.Min(int.MaxValue, gold);
        monster.Carried.Add(coins);
        return true;
    }

    private bool StealItem(Monster monster)
    {
        if (ProtectsBelongings())
        {
            Publish(new MessageEvent("You grab hold of your backpack!"));
            return true;
        }
        var candidates = Player.Inventory.Pack.Where(i => !i.IsArtifact).ToList();
        if (candidates.Count == 0) return false;
        var item = Rng.Pick(candidates);
        var partOfStack = item.Number > 1;
        var stolen = Player.Inventory.Remove(item, 1, () => Objects.NextSerial++);
        Publish(new MessageEvent($"{(partOfStack ? "One of your" : "Your")} {Describe(stolen, withArticle: false)} was stolen!"));
        monster.Carried.Add(stolen);
        RecalculateBonuses();
        return true;
    }

    private void EatFood()
    {
        var food = Player.Inventory.Pack.Where(i => i.Base.Id == "food").ToList();
        if (food.Count == 0) return;
        var item = Rng.Pick(food);
        var name = Describe(item.Clone(0, 1), withArticle: false);
        var partOfStack = item.Number > 1;
        Player.Inventory.Remove(item, 1, () => Objects.NextSerial++);
        Publish(new MessageEvent(partOfStack ? $"One of your {Describe(item, withArticle: false)} was eaten!" : $"Your {name} was eaten!"));
    }

    private void EatLight()
    {
        if (Player.Inventory.Light is not { } light || light.IsArtifact || !light.UsesFuel || light.Fuel <= 0) return;
        light.Fuel = Math.Max(1, light.Fuel - (250 + Rng.RandInt1(250)));
        Publish(new MessageEvent("Your light dims!"));
        RecalculateBonuses();
    }

    /// <summary>Angband EXP_n: drains n d6 plus a percentage of experience; hold life usually protects.</summary>
    private void DrainLife(int dice, int percent)
    {
        var holdLife = Player.HasGearFlag(ItemFlags.HoldLife) || Player.Resists.GetValueOrDefault("hold_life") > 0;
        LearnAboutPlayer(_actingMonster, "hold_life");
        if (holdLife && Rng.Percent(95))
        {
            Publish(new MessageEvent("You keep hold of your life force!"));
            return;
        }
        var amount = Rng.Damroll(dice, 6) + Player.Experience / 100 * percent;
        if (holdLife) amount /= 10;
        Publish(new MessageEvent(holdLife ? "You feel your life slipping away!" : "You feel your life draining away!"));
        LoseExperience(amount);
    }

    /// <summary>Angband DISENCHANT: a random worn weapon or armour loses a point of enchantment.</summary>
    private void Disenchant()
    {
        if (Player.Resists.GetValueOrDefault("disen") > 0)
        {
            Publish(new MessageEvent("You are unaffected!"));
            return;
        }
        var targets = Player.Inventory.Equipped
            .Where(i => i.Base.Slot is not (EquipSlot.Ring or EquipSlot.Amulet or EquipSlot.Light))
            .Where(i => i.ToHit > 0 || i.ToDam > 0 || i.ToAc > 0)
            .ToList();
        if (targets.Count == 0) return;
        var item = Rng.Pick(targets);
        if (item.IsArtifact && Rng.Percent(60))
        {
            Publish(new MessageEvent($"Your {Describe(item, withArticle: false)} resists disenchantment!"));
            return;
        }
        if (item.ToHit > 0) item.ToHit--;
        if (item.ToDam > 0) item.ToDam--;
        if (item.ToAc > 0) item.ToAc--;
        Publish(new MessageEvent($"Your {Describe(item, withArticle: false)} was disenchanted!"));
        RecalculateBonuses();
    }

    /// <summary>Angband DRAIN_CHARGES: a wand or staff in the pack loses its charges, healing the monster.</summary>
    private void DrainCharges(Monster monster)
    {
        var devices = Player.Inventory.Pack.Where(i => i.Charges > 0).ToList();
        if (devices.Count == 0) return;
        var item = Rng.Pick(devices);
        Publish(new MessageEvent("Energy drains from your pack!"));
        monster.Hp = Math.Min(monster.MaxHp, monster.Hp + monster.Race.Depth * item.Charges);
        item.Charges = 0;
    }

    /// <summary>Carried loot falls to the floor when a thief dies.</summary>
    private void DropCarried(Monster monster)
    {
        foreach (var item in monster.Carried) DropNear(item, monster.Position);
        monster.Carried.Clear();
    }

    // --- Movement through walls ----------------------------------------------------------------

    /// <summary>
    /// PASS_WALL monsters drift through rock; KILL_WALL monsters bore through it. Returns null when
    /// the flag doesn't apply (the normal rules decide), otherwise whether the monster moved.
    /// </summary>
    private bool? TryWallStep(Monster monster, TerrainDef feature, Geometry.Loc target)
    {
        if (feature.Has(TerrainFlags.Passable) || feature.Has(TerrainFlags.Permanent) || !Level.InBoundsFully(target)) return null;
        var race = monster.Race;
        if (race.Has(MonsterFlags.PassWall))
        {
            if (monster.IsVisible) LearnMonsterFlag(race, MonsterFlags.PassWall);
            Level.Monsters.Move(monster, target);
            monster.WanderStuck = 0;
            return true;
        }
        if (race.Has(MonsterFlags.KillWall) && feature.HasAny(TerrainFlags.Rock | TerrainFlags.DoorAny))
        {
            ref var sq = ref Level[target];
            sq.Feature = Data.Terrain.Ids.Floor;
            sq.Flags &= ~SquareFlags.AnyWallMarker;
            sq.LockPower = 0;
            if (Level[target].Has(SquareFlags.View)) Publish(new MessageEvent("You hear a grinding noise."));
            if (monster.IsVisible) LearnMonsterFlag(race, MonsterFlags.KillWall);
            Level.Monsters.Move(monster, target);
            monster.WanderStuck = 0;
            UpdateView();
            return true;
        }
        return null;
    }
}
