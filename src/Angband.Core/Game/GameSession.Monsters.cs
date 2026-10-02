using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>
/// Monster AI (Angband mon-move.c, simplified). Each turn a monster: wakes gradually, may breed,
/// may cast a spell, then moves. Movement priorities: erratic/confused → random; afraid → flee
/// toward safety (fighting back if cornered); pack animals lurk out of sight while the player is
/// in a corridor; otherwise hunt by sight, sound (the player's noise flow) or scent, and when the
/// player cannot be sensed at all, wander.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The player's scent trail (followed by monsters with a sense of smell).</summary>
    public ScentMap Scent { get; } = new();

    /// <summary>One monster's turn, on demand (for tests).</summary>
    internal int RunMonsterTurn(Monster monster) => MonsterTurn(monster);

    /// <summary>
    /// Angband process_monsters for one monster: a mimic keeps still; an inactive monster (one that
    /// can't sense the player, and is unhurt) does nothing at all; then its sleep and conditions
    /// (process_monster_timed); then monster_turn — webs, rousing its group, breeding, a spell,
    /// and moving or attacking.
    /// </summary>
    private int MonsterTurn(Monster monster)
    {
        if (Player.IsDead || !monster.IsActive) return EnergyTable.MoveEnergy;
        if (monster.Camouflaged && monster.MimicItem is not null) return EnergyTable.MoveEnergy;
        if (!CheckActive(monster)) return EnergyTable.MoveEnergy;
        if (ProcessMonsterTimed(monster)) return EnergyTable.MoveEnergy;

        var race = monster.Race;
        if (StuckInWeb(monster)) return EnergyTable.MoveEnergy;
        GroupRouse(monster);
        if (race.Has(MonsterFlags.Multiply) && TryMultiply(monster)) return EnergyTable.MoveEnergy;
        if (monster.IsVisible) Lore.For(race.Id).TurnsWatched++;
        if (MakeRangedAttack(monster)) return EnergyTable.MoveEnergy;
        MonsterMoveTurn(monster);
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// Angband monster_turn: a monster in a web passes through it (PASS_WEB, or passing walls), tears
    /// it down on the way (tunnelling through walls), spends its turn clearing it (CLEAR_WEB), or is
    /// stuck. True when the turn is used up.
    /// </summary>
    private bool StuckInWeb(Monster monster)
    {
        if (!IsWebbed(monster.Position)) return false;
        var race = monster.Race;
        if (race.Has("PASS_WEB") || race.Has("PASS_WALL")) return false;
        if (race.Has("KILL_WALL"))
        {
            ClearWeb(monster.Position);
            return false;
        }
        if (race.Has("CLEAR_WEB")) ClearWeb(monster.Position);
        return true;
    }

    /// <summary>
    /// Angband monster_take_terrain_damage: lava burns a monster that isn't immune to fire (100 + 1d100);
    /// one that dies of it is gone, dropping what it carried, and no one earns anything.
    /// </summary>
    private void MonsterTerrainDamage(Monster monster)
    {
        if (!Level.Has(monster.Position, TerrainFlags.Fiery) || monster.Race.Has("IM_FIRE")) return;
        var name = Capitalize(MonsterName(monster));
        monster.Hp -= 100 + Rng.RandInt1(100);
        if (monster.Hp >= 0)
        {
            if (monster.IsVisible) Publish(new MessageEvent($"{name} catches fire!"));
            return;
        }
        if (monster.IsVisible) Publish(new MessageEvent($"{name} disintegrates!"));
        QuestKill(monster);
        Level.Monsters.Remove(monster);
        DropCarried(monster);
        DropMonsterLoot(monster);
        if (monster == Commanded) ReleaseCommand(announce: false);
    }

    /// <summary>
    /// Angband process_monster_multiply: breeders reproduce into an adjacent empty square, less often
    /// the more crowded they are, up to a level-wide cap.
    /// </summary>
    private bool TryMultiply(Monster monster)
    {
        var breeders = Level.Monsters.All.Count(m => m.Race.Has(MonsterFlags.Multiply));
        if (breeders >= Data.Constants.MaxBreeders) return false;
        if (BreederSpent(monster.Race)) return false;

        // Angband counts the monsters in the 3x3 block around the breeder, the breeder itself among them.
        var crowd = Level.Monsters.All.Count(m => m.Position.ChebyshevTo(monster.Position) <= 1);
        if (crowd >= 4) return false;
        if (!Rng.OneIn(crowd * Data.Constants.BreedRate)) return false;

        var spots = Level.Neighbors(monster.Position)
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != Player.Position)
            .ToList();
        if (spots.Count == 0) return false;

        var child = _spawner.Place(Level, Rng, monster.Race, Rng.Pick(spots), asleep: false);
        Scheduler.Add(child);
        CountBirth(monster.Race);
        child.IsVisible = MonsterVisible(child);
        Publish(new MonsterBredEvent(monster.Race.Id, child.Position, child.IsVisible));
        if (child.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.Multiply);
        return true;
    }

    /// <summary>
    /// Angband monster_reduce_sleep: a sleeping monster that can hear the player may stir, more often
    /// the noisier (less stealthy) and closer the player is.
    /// </summary>
    /// <summary>
    /// Angband monster_reduce_sleep: aggravation wakes it outright; otherwise, if a cube of 0..1023
    /// is within the player's noise (2^(30 - stealth)), its sleep drops by 1 — by 100 / the noise
    /// here, close enough to hear.
    /// </summary>
    private void ReduceSleep(Monster monster)
    {
        if (Player.HasGearFlag(ItemFlags.Aggravate))
        {
            WakeMonster(monster);
            if (monster.IsVisible) LearnRune(RuneIds.Flag(ItemFlags.Aggravate));
            return;
        }
        long playerNoise = 1L << Math.Clamp(30 - Player.Stealth - RaceStealthAgainst(monster), 0, 62);
        long notice = Rng.RandInt0(1024);
        if (notice * notice * notice > playerNoise) return;
        var noise = Noise[monster.Position];
        var reduction = noise is > 0 and < 50 ? 100 / noise : 1;
        monster.Sleep = Math.Max(0, monster.Sleep - reduction);
        if (monster.Sleep == 0)
        {
            monster.IsVisible = MonsterVisible(monster);
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wakes up."));
        }
    }

    /// <summary>Monsters regenerate every 100 game turns (Angband regen_monster; their conditions wear off on their own turns).</summary>
    private void MonsterUpkeep(long gameTurn)
    {
        foreach (var monster in Level.Monsters.All.ToList())
        {
            if (gameTurn % 100 == 0 && monster.Hp < monster.MaxHp)
            {
                var gain = monster.MaxHp / 100;
                if (gain == 0 && Rng.OneIn(2)) gain = 1;
                if (monster.Race.Has(MonsterFlags.Regenerate)) gain *= 2;
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + gain);
            }
        }
    }
}
