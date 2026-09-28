using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Angband 4.2's racial abilities (p_race.txt obj-flags and player-flags).</summary>
public class RaceAbilityTests
{
    private static GameSession As(string race, string cls = "warrior")
    {
        var arena = Arena.Create(9,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,@,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        var game = GameSession.NewGame(TestData.Game, 9, CharacterSpec.Default(race, cls));
        game.UseLevel(arena.Level, arena.Player.Position);
        TestGames.ClearMonsters(game);
        game.UpdateView();
        return game;
    }

    [Theory]
    [InlineData("half_elf", "sust_dex")]
    [InlineData("elf", "sust_dex")]
    [InlineData("hobbit", "hold_life")]
    [InlineData("half_troll", "sust_str")]
    [InlineData("dunadan", "sust_con")]
    [InlineData("high_elf", "see_invis")]
    public void EachRace_HasItsAngbandAbility(string race, string ability) =>
        Assert.True(As(race).Player.Resists.GetValueOrDefault(ability) > 0);

    [Fact]
    public void AnElf_KeepsItsDexterity()
    {
        var game = As("elf");
        Assert.False(game.DrainStat("dex"));
        Assert.True(As("human").DrainStat("dex"));
    }

    [Fact]
    public void AHighElf_SeesInvisibleThings()
    {
        var high = As("high_elf");
        var ghost = Arena.AddMonster(high, "poltergeist", high.Player.Position + new Loc(2, 0));
        high.UpdateView();
        Assert.True(ghost.IsVisible);

        var human = As("human");
        var other = Arena.AddMonster(human, "poltergeist", human.Player.Position + new Loc(2, 0));
        human.UpdateView();
        Assert.False(other.IsVisible);
    }

    [Fact]
    public void AHobbit_KnowsMushroomsOnPickingThemUp()
    {
        var game = As("hobbit");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var mushroom = game.Objects.Create("mushroom_of_second_sight");
        Assert.False(game.Knowledge.IsAware(mushroom.Kind));
        game.Level.Objects.Add(game.Player.Position, mushroom);
        game.Execute(new PickupCommand(mushroom));
        Assert.True(game.Knowledge.IsAware(mushroom.Kind));
        Assert.Contains("Mushrooms for breakfast!", said);
    }

    [Fact]
    public void AGnome_KnowsWandsOnPickingThemUp_AndOthersDont()
    {
        foreach (var (race, knows) in new[] { ("gnome", true), ("human", false) })
        {
            var game = As(race);
            var wand = game.Objects.Create("wand_of_stinking_cloud");
            game.Level.Objects.Add(game.Player.Position, wand);
            game.Execute(new PickupCommand(wand));
            Assert.Equal(knows, game.Knowledge.IsAware(wand.Kind));
        }
    }

    [Fact]
    public void ADwarf_SensesTreasureInTheRock_WhenClearHeaded()
    {
        var game = As("dwarf");
        var vein = game.Player.Position + new Loc(2, -3); // in the wall above
        game.Level[vein].Feature = game.Data.Terrain.Ids.MagmaTreasure;
        game.Known.Forget(vein);
        game.Execute(new HoldCommand());
        Assert.True(game.Known.IsKnown(vein));

        var dazed = As("dwarf");
        dazed.Level[vein].Feature = dazed.Data.Terrain.Ids.MagmaTreasure;
        dazed.Known.Forget(vein);
        dazed.Player.Timed.Set(dazed.Data.Timed(TimedIds.Confused)!, 50);
        dazed.Execute(new HoldCommand());
        Assert.False(dazed.Known.IsKnown(vein));
    }

    [Fact]
    public void AnOlderSave_GainsItsRacesNewAbilities()
    {
        var game = As("high_elf");
        game.Player.IntrinsicResists.Remove("see_invis"); // as saved before this was added
        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal(1, loaded.Player.IntrinsicResists["see_invis"]);
    }
}
