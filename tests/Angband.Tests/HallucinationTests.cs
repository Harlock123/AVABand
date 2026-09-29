using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Hallucination (Angband TMD_IMAGE): where it comes from, what it spoils, and what cures it.</summary>
public class HallucinationTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(4,
            "#############",
            "#,,,,,,,,,,,#",
            "#,,,,,@,,,,,#",
            "#,,,,,,,,,,,#",
            "#############");
        TestGames.ClearMonsters(game);
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static void Eat(GameSession game, string kindId)
    {
        var item = game.Objects.Create(kindId);
        game.Player.Inventory.Add(item);
        Assert.True(game.Execute(new UseCommand(item)));
    }

    [Fact]
    public void TheMushroomOfEmergency_Drugs_AndClearMind_Clears()
    {
        var game = Game();
        var messages = Messages(game);
        Eat(game, "mushroom_of_emergency");
        Assert.True(game.IsHallucinating);
        Assert.InRange(game.Player.Timed[TimedIds.Image], 30, 50); // 5d5+25
        Assert.Contains("You feel drugged!", messages);

        Eat(game, "mushroom_of_clear_mind");
        Assert.False(game.IsHallucinating);
        Assert.Contains("You can see clearly again.", messages);
    }

    [Fact]
    public void ResistingChaos_KeepsTheMindClear()
    {
        var game = Game();
        game.Player.Resists["chaos"] = 1;
        Assert.False(game.IncreaseTimed(TimedIds.Image, 20));
        Assert.False(game.IsHallucinating);
    }

    [Fact]
    public void AMagicMushroomPatch_CanMakeYouHallucinate()
    {
        // 4.2's HALLU blow: 3 + 1d(level/2) turns of hallucination — no longer a stand-in for confusion.
        var game = Game();
        game.Player.Hp = game.Player.MaxHp = 10_000;
        var patch = Arena.AddMonster(game, "magic_mushroom_patch", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 200 && !game.IsHallucinating; i++)
        {
            game.Player.Timed.Set(game.Data.Timed(TimedIds.Confused)!, 0);
            game.RunMonsterTurn(patch);
        }
        Assert.True(game.IsHallucinating);
        Assert.InRange(game.Player.Timed[TimedIds.Image], 4, 2 * (3 + 15 / 2)); // it has two such blows
    }

    [Fact]
    public void Chaos_Hallucinates_Confuses_AndDrains_UnlessResisted()
    {
        var game = Game();
        game.Player.Hp = game.Player.MaxHp = 10_000;
        game.GainExperience(10_000);
        var exp = game.Player.Experience;
        var messages = Messages(game);

        game.ElementalHit("chaos", 10, "a chaos breath");
        Assert.True(game.IsHallucinating);
        Assert.True(game.Player.Timed.Has(TimedIds.Confused));
        Assert.True(game.Player.Experience < exp);
        Assert.Contains("You feel your life force draining away!", messages);

        var resistant = Game();
        resistant.Player.Hp = resistant.Player.MaxHp = 10_000;
        resistant.Player.Resists["chaos"] = 1;
        var said = Messages(resistant);
        resistant.ElementalHit("chaos", 10, "a chaos breath");
        Assert.False(resistant.IsHallucinating);
        Assert.False(resistant.Player.Timed.Has(TimedIds.Confused));
        Assert.Contains("You resist the effect!", said);
    }

    [Fact]
    public void Hallucinating_SpoilsDisarming_Devices_AndSeeingClearly()
    {
        var game = Game();
        var wand = game.Objects.Create("wand_of_magic_missile");
        var disarm = game.EffectiveDisarmSkill;
        var fail = game.DeviceFailChance(wand);
        var jackal = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(2, 0));
        game.UpdateView();
        Assert.StartsWith("You can see", game.MonsterList()[0].Text);

        game.IncreaseTimed(TimedIds.Image, 50);

        Assert.Equal(disarm / 10, game.EffectiveDisarmSkill);
        Assert.True(game.DeviceFailChance(wand) > fail);
        Assert.Equal("something strange", game.LookDescription(jackal));
        var list = Assert.Single(game.MonsterList());
        Assert.Equal("Your hallucinations are too wild to see things clearly.", list.Text);
    }
}
