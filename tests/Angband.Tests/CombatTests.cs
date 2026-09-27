using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;

namespace Angband.Tests;

public class CombatTests
{
    private static readonly Loc Start = new(5, 2);

    private static int AttackUntilDead(GameSession game, Monster monster, Direction dir, int maxTurns = 200)
    {
        var turns = 0;
        while (monster.IsActive && turns++ < maxTurns) Assert.True(game.Execute(new WalkCommand(dir)));
        return turns;
    }

    [Fact]
    public void BumpingAMonster_AttacksAndKills_GrantingExperience()
    {
        var game = Arena.Create();
        game.Player.Hp = game.Player.MaxHp = 1000; // survive long enough to finish the fight
        // A grey mold never moves and never panics, so it stays put until it dies.
        var mold = Arena.AddMonster(game, "grey_mold", Start.Step(Direction.East));
        var events = new List<IGameEvent>();
        using var _ = game.Events.SubscribeAll(events.Add);

        AttackUntilDead(game, mold, Direction.East);

        Assert.False(mold.IsActive);
        Assert.Null(game.Level.Monsters.At(Start.Step(Direction.East)));
        Assert.Contains(events, e => e is MonsterKilledEvent { RaceId: "grey_mold" });
        Assert.Equal(3 * 1, game.Player.Experience); // exp 3 × level 1 / player level 1
        Assert.Contains(events, e => e is PlayerAttackEvent { Hit: true });
    }

    [Fact]
    public void FractionalBlows_LeaveEnergyOver()
    {
        var game = Arena.Create();
        var mold = Arena.AddMonster(game, "grey_mold", Start.Step(Direction.East));
        mold.Hp = mold.MaxHp = 100_000;
        game.Player.Blows = 250; // 2.5 blows: two blows cost 80 energy
        var turn = game.GameTurn;

        game.Execute(new WalkCommand(Direction.East));

        Assert.Equal(turn + 8, game.GameTurn); // 20 energy left, +10 per game turn at normal speed
    }

    [Fact]
    public void AwakeMonster_HuntsAndBitesThePlayer()
    {
        var game = Arena.Create();
        game.Player.Hp = game.Player.MaxHp = 1000;
        Arena.AddMonster(game, "jackal", new Loc(1, 1));
        var hurt = 0;
        var damage = 0;
        using var _ = game.Events.Subscribe<MonsterAttackEvent>(e => { if (e.Hit) hurt++; });
        // Regeneration at 1000 hit points heals a bite within a turn, so count the damage itself.
        using var __ = game.Events.Subscribe<PlayerHurtEvent>(e => damage += e.Damage);

        for (var i = 0; i < 200 && hurt == 0; i++) game.Execute(new HoldCommand());

        Assert.True(hurt > 0);
        Assert.True(damage > 0);
    }

    [Fact]
    public void MonstersFollowNoise_AroundCorners()
    {
        var game = Arena.Create(1,
            "###########",
            "#,,,,,,,,,#",
            "#########,#",
            "#,,,,,,,,,#",
            "#,@########",
            "###########");
        game.Player.Hp = game.Player.MaxHp = 1000;
        // The player is 17 steps away by sound but only 3 in a straight line; a kobold hears 20.
        var kobold = Arena.AddMonster(game, "kobold", new Loc(1, 1));

        for (var i = 0; i < 40 && kobold.Position.ChebyshevTo(game.Player.Position) > 1; i++)
            game.Execute(new HoldCommand());

        Assert.Equal(1, kobold.Position.ChebyshevTo(game.Player.Position));
    }

    [Fact]
    public void SleepingMonster_DoesNotAct_UntilWoken()
    {
        var game = Arena.Create();
        game.Player.Stealth = 30; // silent: monsters never stir
        var kobold = Arena.AddMonster(game, "kobold", Start.Step(Direction.West), awake: false);
        Assert.True(kobold.IsAsleep);

        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);

        game.Execute(new WalkCommand(Direction.West));
        Assert.False(kobold.IsAsleep);
    }

    [Fact]
    public void AfraidPlayer_CannotMelee()
    {
        var game = Arena.Create();
        var kobold = Arena.AddMonster(game, "kobold", Start.Step(Direction.East));
        game.IncreaseTimed(TimedIds.Afraid, 50);
        var hp = kobold.Hp;
        var messages = new List<string>();
        using var _ = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        game.Execute(new WalkCommand(Direction.East));

        Assert.Equal(hp, kobold.Hp);
        Assert.Contains(messages, m => m.StartsWith("You are too afraid to attack"));
    }

    [Fact]
    public void Sling_ShootsTheNearestMonster()
    {
        var game = Arena.Create();
        game.Player.SkillBow = 500; // hit every time the dice allow
        var ant = Arena.AddMonster(game, "grey_mold", new Loc(9, 2)); // stationary target
        var shots = game.Player.Inventory.Quiver.Sum(q => q.Number);
        var fired = new List<MissileFiredEvent>();
        using var _ = game.Events.Subscribe<MissileFiredEvent>(fired.Add);

        for (var i = 0; i < 20 && ant.IsActive; i++) game.Execute(new FireCommand());

        Assert.False(ant.IsActive);
        Assert.True(game.Player.Inventory.Quiver.Sum(q => q.Number) < shots);
        Assert.All(fired, f => Assert.Contains(new Loc(9, 2), f.Path));
    }

    [Fact]
    public void Firing_WithoutATarget_TakesNoTime()
    {
        var game = Arena.Create();
        var turn = game.GameTurn;
        Assert.False(game.Execute(new FireCommand()));
        Assert.Equal(turn, game.GameTurn);
    }

    [Fact]
    public void PoisonBite_PoisonsThePlayer_UnlessResisted()
    {
        var game = Arena.Create(3);
        game.Player.Hp = game.Player.MaxHp = 1000;
        Arena.StripGear(game);
        Arena.AddMonster(game, "white_worm_mass", Start.Step(Direction.East));
        for (var i = 0; i < 200 && !game.Player.Timed.Has(TimedIds.Poisoned); i++) game.Execute(new HoldCommand());
        Assert.True(game.Player.Timed.Has(TimedIds.Poisoned));

        var immune = Arena.Create(3);
        immune.Player.Hp = immune.Player.MaxHp = 1000;
        Arena.StripGear(immune);
        immune.Player.IntrinsicResists["pois"] = 1;
        immune.RecalculateBonuses();
        Arena.AddMonster(immune, "white_worm_mass", Start.Step(Direction.East));
        for (var i = 0; i < 200; i++) immune.Execute(new HoldCommand());
        Assert.False(immune.Player.Timed.Has(TimedIds.Poisoned));
    }

    [Fact]
    public void Poison_HurtsOverTime_AndWearsOff()
    {
        var game = Arena.Create();
        game.Player.Hp = game.Player.MaxHp = 100;
        game.IncreaseTimed(TimedIds.Poisoned, 5);

        for (var i = 0; i < 10; i++) game.Execute(new HoldCommand());

        Assert.False(game.Player.Timed.Has(TimedIds.Poisoned));
        Assert.True(game.Player.Hp < 100);
    }

    [Fact]
    public void Paralysis_CostsTurns()
    {
        var game = Arena.Create();
        game.IncreaseTimed(TimedIds.Paralyzed, 5);
        var turn = game.GameTurn;

        game.Execute(new HoldCommand());

        Assert.False(game.Player.IsIncapacitated);
        Assert.True(game.GameTurn - turn >= 50, $"only {game.GameTurn - turn} game turns passed");
    }

    [Fact]
    public void Confusion_SometimesSendsThePlayerAstray()
    {
        var game = Arena.Create(9);
        var strays = 0;
        for (var i = 0; i < 40; i++)
        {
            game.IncreaseTimed(TimedIds.Confused, 10);
            game.Player.Position = Start;
            game.Execute(new WalkCommand(Direction.East));
            if (game.Player.Position != Start.Step(Direction.East)) strays++;
        }
        Assert.InRange(strays, 4, 30);
    }

    [Fact]
    public void FireBlows_AreResisted()
    {
        int FireDamage(int resist)
        {
            var game = Arena.Create(5);
            game.Player.Hp = game.Player.MaxHp = 100_000;
            if (resist != 0) game.Player.IntrinsicResists["fire"] = resist;
            game.RecalculateBonuses();
            var hurt = 0;
            using var _ = game.Events.Subscribe<PlayerHurtEvent>(e => hurt += e.Damage);
            var giant = Arena.AddMonster(game, "fire_giant", Start.Step(Direction.East));
            for (var i = 0; i < 50; i++) game.Execute(new HoldCommand());
            return hurt;
        }

        var normal = FireDamage(0);
        var resisted = FireDamage(1);
        Assert.Equal(0, FireDamage(3));
        Assert.InRange(resisted, normal / 4, normal / 2);
    }

    [Fact]
    public void Dying_EndsTheGame()
    {
        var game = Arena.Create();
        game.Player.Hp = game.Player.MaxHp = 1;
        Arena.StripGear(game);
        Arena.AddMonster(game, "fire_giant", Start.Step(Direction.East));
        PlayerDiedEvent? died = null;
        using var _ = game.Events.Subscribe<PlayerDiedEvent>(e => died = e);

        for (var i = 0; i < 50 && !game.IsGameOver; i++) game.Execute(new HoldCommand());

        Assert.True(game.IsGameOver);
        Assert.Equal("a fire giant", died?.KilledBy);
        Assert.False(game.Execute(new HoldCommand()));
    }

    [Fact]
    public void Resting_RestoresHitPoints()
    {
        var game = Arena.Create();
        game.Player.MaxHp = 50;
        game.Player.Hp = 5;

        Assert.True(game.Execute(new RestCommand()));

        Assert.Equal(50, game.Player.Hp);
    }

    [Fact]
    public void Resting_IsRefusedWithMonstersInView()
    {
        var game = Arena.Create();
        game.Player.Hp = 5;
        Arena.AddMonster(game, "grey_mold", new Loc(9, 3));
        Assert.False(game.Execute(new RestCommand()));
    }

    [Fact]
    public void FrightenedMonsters_RunAway()
    {
        var game = Arena.Create(1,
            "###################",
            "#,,,,,,,,,,,,,,,,,#",
            "#,,,,@,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,#",
            "###################");
        var kobold = Arena.AddMonster(game, "kobold", new Loc(7, 2));
        kobold.Fear = 100;

        for (var i = 0; i < 5; i++) game.Execute(new HoldCommand());

        Assert.True(kobold.Position.DistanceTo(game.Player.Position) > 2);
    }

    [Fact]
    public void KilledUniques_AreNeverGeneratedAgain()
    {
        var spawner = new MonsterSpawner(TestData.Game);
        var rng = new Angband.Core.Randomness.GameRandom(1);
        var excluded = new HashSet<string> { "fang", "grip" };
        for (var i = 0; i < 2000; i++)
        {
            var race = spawner.PickRace(rng, 5, excluded);
            Assert.NotNull(race);
            Assert.DoesNotContain(race!.Id, excluded);
        }
    }

    [Fact]
    public void TownHasOnlyTownMonsters_DungeonHasNone()
    {
        var spawner = new MonsterSpawner(TestData.Game);
        var rng = new Angband.Core.Randomness.GameRandom(2);
        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(0, spawner.PickRace(rng, 0, new HashSet<string>())!.Depth);
            Assert.True(spawner.PickRace(rng, 3, new HashSet<string>())!.Depth > 0);
        }
    }

    [Fact]
    public void NewLevels_ArePopulated()
    {
        var game = GameSession.NewGame(TestData.Game, 12);
        game.Execute(new DebugJumpCommand(5));
        Assert.True(game.Level.Monsters.Count >= 10);
        Assert.All(game.Level.Monsters.All, m =>
        {
            Assert.True(game.Level.IsPassable(m.Position) || m.Race.Has(Angband.Core.Definitions.MonsterFlags.PassWall), $"{m} on {game.Level.FeatureAt(m.Position).Id}");
            Assert.Equal(m.Id, game.Level[m.Position].Monster);
            Assert.Contains(m, game.Scheduler.Actors);
        });
    }

    [Fact]
    public void FightsAreDeterministic()
    {
        static string Fight()
        {
            var game = Arena.Create(77);
            game.Player.Hp = game.Player.MaxHp = 500;
            var orc = Arena.AddMonster(game, "cave_orc", Start.Step(Direction.East));
            for (var i = 0; i < 30 && orc.IsActive; i++) game.Execute(new WalkCommand(Direction.East));
            return $"{game.Player.Hp} {orc.Hp} {game.GameTurn} {game.Player.Experience}";
        }
        Assert.Equal(Fight(), Fight());
    }
}
