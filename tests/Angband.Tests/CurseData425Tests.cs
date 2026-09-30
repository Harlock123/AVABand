using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's curses (curse.txt, obj-curse.c): all 27, with powers, timers and conflicts.</summary>
public class CurseData425Tests
{
    private static CurseDef Curse(string id) => TestData.Game.Curses.Single(c => c.Id == id);

    [Fact]
    public void All_of_4_2_5s_curses_are_here()
    {
        Assert.Equal(27, TestData.Game.Curses.Count(c => c.Id != "devouring")); // (and AVABand's Bag of Devouring's own)
        Assert.Equal(-50, Curse("vulnerability").ToAc);
        Assert.Equal(["cold"], Curse("burning_up").Resists);
        Assert.Contains("NO_TELEPORT", Curse("anti_teleportation").Flags);
    }

    [Fact]
    public void Conflicting_curses_keep_apart_and_a_stronger_one_only_grows()
    {
        var f = new Angband.Core.Items.ObjectFactory(TestData.Game);
        var rng = new GameRandom(1);
        var amulet = f.Create("amulet_of_infravision");
        Assert.True(f.AddCurse(rng, amulet, Curse("teleportation"), 30));
        Assert.False(f.AddCurse(rng, amulet, Curse("anti_teleportation"), 30)); // they conflict
        Assert.False(f.AddCurse(rng, amulet, Curse("teleportation"), 10));      // weaker: no change
        Assert.True(f.AddCurse(rng, amulet, Curse("teleportation"), 50));
        Assert.Equal(50, amulet.CursePower("teleportation"));

        var freeAction = f.Create("ring_of_free_action");
        Assert.False(f.AddCurse(rng, freeAction, Curse("paralysis"), 30)); // foiled by free action
    }

    [Fact]
    public void Anti_teleportation_forbids_teleporting()
    {
        var game = Arena.Create(2);
        var boots = game.Player.Inventory.Add(game.Objects.Create("leather_boots"))!;
        boots.Curses.Add("anti_teleportation");
        game.Execute(new WieldCommand(boots));
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var at = game.Player.Position;
        game.Execute(new UseCommand(game.Player.Inventory.Add(game.Objects.Create("phase_door"))!));
        Assert.Equal(at, game.Player.Position);
        Assert.Contains("Teleportation forbidden!", said);
    }

    [Fact]
    public void Dullness_drains_the_mind()
    {
        var game = Arena.Create(3);
        var intel = game.Player.Stats["int"];
        var cap = game.Player.Inventory.Add(game.Objects.Create("hard_leather_cap"))!;
        cap.Curses.Add("dullness");
        game.Execute(new WieldCommand(cap));
        Assert.Equal(Math.Max(3, intel - 5), game.Player.Stats["int"]);
    }

    [Fact]
    public void A_curse_acts_on_its_own_timer()
    {
        var game = Arena.Create(4);
        game.Player.Hp = game.Player.MaxHp = 5000;
        var sword = game.Player.Inventory.Weapon!;
        sword.Curses.Add("treacherous_weapon");
        sword.CurseTimeouts["treacherous_weapon"] = 3;
        game.RecalculateBonuses();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        for (var i = 0; i < 3; i++) game.Execute(new HoldCommand());
        Assert.Contains("Your weapon turns on you!", said);
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("treacherous_weapon")));
    }

    [Fact]
    public void Curse_powers_are_saved()
    {
        var game = Arena.Create(5);
        var sword = game.Player.Inventory.Weapon!;
        sword.Curses.Add("air_swing");
        sword.CursePowers["air_swing"] = 42;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(42, loaded.Player.Inventory.Weapon!.CursePower("air_swing"));
    }
}
