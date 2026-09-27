using Angband.Core.Time;

namespace Angband.Tests;

public class TurnSchedulerTests
{
    private sealed class Actor(int id, int speed, bool player = false) : IActor
    {
        public int ActorId { get; } = id;
        public int Speed { get; set; } = speed;
        public int Energy { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPlayer { get; } = player;
        public int Turns { get; set; }
    }

    private sealed class Handler : ITurnHandler
    {
        public List<int> Order { get; } = [];
        public int WorldTicks { get; private set; }
        public Func<IActor, int> Cost { get; set; } = _ => EnergyTable.MoveEnergy;
        public Action<IActor>? OnAct { get; set; }

        public bool NeedsInput(IActor actor) => ((Actor)actor).IsPlayer;

        public int TakeTurn(IActor actor)
        {
            ((Actor)actor).Turns++;
            Order.Add(actor.ActorId);
            OnAct?.Invoke(actor);
            return Cost(actor);
        }

        public void OnWorldTick(long gameTurn) => WorldTicks++;
    }

    [Fact]
    public void NormalSpeed_ActsOncePerTenGameTurns()
    {
        var scheduler = new TurnScheduler();
        var monster = new Actor(1, 0);
        scheduler.Add(monster);
        var handler = new Handler();

        scheduler.Advance(handler, maxGameTurns: 1000);

        Assert.Equal(100, monster.Turns);
        Assert.Equal(1000, scheduler.GameTurn);
        Assert.Equal(100, handler.WorldTicks);
    }

    [Theory]
    [InlineData(10, 2.0)]
    [InlineData(20, 3.0)]
    [InlineData(-10, 0.5)]
    public void Speed_ScalesActionRate(int speed, double ratio)
    {
        var scheduler = new TurnScheduler();
        var normal = new Actor(1, 0);
        var other = new Actor(2, speed);
        scheduler.Add(normal);
        scheduler.Add(other);

        scheduler.Advance(new Handler(), maxGameTurns: 10_000);

        Assert.InRange((double)other.Turns / normal.Turns, ratio * 0.98, ratio * 1.02);
    }

    [Fact]
    public void PlayerAwaitingInput_StopsTheClock()
    {
        var scheduler = new TurnScheduler();
        var player = new Actor(0, 0, player: true);
        scheduler.Add(player);
        var handler = new Handler();

        var ready = scheduler.Advance(handler);
        Assert.Same(player, ready);
        Assert.Equal(10, scheduler.GameTurn);

        // Nothing moves until the player spends energy.
        player.Energy -= EnergyTable.MoveEnergy;
        Assert.Same(player, scheduler.Advance(handler));
        Assert.Equal(20, scheduler.GameTurn);
    }

    [Fact]
    public void FastMonster_ActsTwicePerPlayerTurn()
    {
        var scheduler = new TurnScheduler();
        var player = new Actor(0, 0, player: true);
        var fast = new Actor(1, 10);
        scheduler.Add(player);
        scheduler.Add(fast);
        var handler = new Handler();

        scheduler.Advance(handler);
        for (var i = 0; i < 10; i++)
        {
            player.Energy -= EnergyTable.MoveEnergy;
            scheduler.Advance(handler);
        }

        Assert.InRange(fast.Turns, 20, 22);
    }

    [Fact]
    public void MostEnergeticActsFirst_TiesByActorId()
    {
        var scheduler = new TurnScheduler();
        var a = new Actor(3, 0) { Energy = 95 };
        var b = new Actor(1, 0) { Energy = 90 };
        var c = new Actor(2, 0) { Energy = 90 };
        scheduler.Add(a);
        scheduler.Add(b);
        scheduler.Add(c);
        var handler = new Handler();

        scheduler.Advance(handler, maxGameTurns: 1);

        Assert.Equal([3, 1, 2], handler.Order);
    }

    [Fact]
    public void ActorKilledMidTurn_DoesNotAct()
    {
        var scheduler = new TurnScheduler();
        var killer = new Actor(1, 0) { Energy = 95 };
        var victim = new Actor(2, 0) { Energy = 95 };
        scheduler.Add(killer);
        scheduler.Add(victim);
        var handler = new Handler { OnAct = a => { if (a.ActorId == 1) victim.IsActive = false; } };

        scheduler.Advance(handler, maxGameTurns: 50);

        Assert.Equal(0, victim.Turns);
        Assert.DoesNotContain(victim, scheduler.Actors);
    }

    [Fact]
    public void ExpensiveActions_DelayNextTurn()
    {
        var scheduler = new TurnScheduler();
        var slowpoke = new Actor(1, 0);
        scheduler.Add(slowpoke);
        var handler = new Handler { Cost = _ => 2 * EnergyTable.MoveEnergy };

        scheduler.Advance(handler, maxGameTurns: 1000);

        Assert.InRange(slowpoke.Turns, 49, 51);
    }

    [Fact]
    public void SameSetup_IsDeterministic()
    {
        List<int> Run()
        {
            var scheduler = new TurnScheduler();
            for (var i = 0; i < 6; i++) scheduler.Add(new Actor(i + 1, i * 3 - 5));
            var handler = new Handler();
            scheduler.Advance(handler, maxGameTurns: 500);
            return handler.Order;
        }

        Assert.Equal(Run(), Run());
    }
}
