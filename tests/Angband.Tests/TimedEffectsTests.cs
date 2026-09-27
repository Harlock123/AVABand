using Angband.Core.Definitions;
using Angband.Core.Effects;

namespace Angband.Tests;

public class TimedEffectsTests
{
    private static TimedEffectDef Def(string id) => TestData.Game.Timed(id)!;

    [Fact]
    public void Increase_ReportsBeginThenIncrease()
    {
        var t = new TimedEffects();
        Assert.Equal("You are poisoned!", t.Increase(Def(TimedIds.Poisoned), 5));
        Assert.Equal("You are more poisoned!", t.Increase(Def(TimedIds.Poisoned), 5));
        Assert.Equal(10, t[TimedIds.Poisoned]);
    }

    [Fact]
    public void NoStack_IgnoresNewDosesWhileActive()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Paralyzed), 5);
        Assert.Null(t.Increase(Def(TimedIds.Paralyzed), 50));
        Assert.Equal(5, t[TimedIds.Paralyzed]);
    }

    [Fact]
    public void Decrease_ReportsEndAtZero()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Confused), 2);
        Assert.Null(t.Decrease(Def(TimedIds.Confused), 1));
        Assert.Equal("You feel less confused now.", t.Decrease(Def(TimedIds.Confused), 1));
        Assert.False(t.Has(TimedIds.Confused));
    }

    [Fact]
    public void Increase_IsCappedAtMax()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Stun), 5000);
        Assert.Equal(Def(TimedIds.Stun).Max, t[TimedIds.Stun]);
    }
}
