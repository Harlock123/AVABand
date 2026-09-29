using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2.5's traps (trap.txt, trap.c): all 35 of them, chosen by rarity, hidden until your
/// search skill is up to their power, saved against by gear, armour or a saving throw, some gone
/// once sprung, some sprung as you leave, and runes disarmed with the magical skill.
/// </summary>
public class TrapData425Tests
{
    private static readonly string[] Room =
    [
        "#########",
        "#,,,,,,,#",
        "#,,,@,,,#",
        "#,,,,,,,#",
        "#########",
    ];

    private static GameSession Game(ulong seed = 3)
    {
        var game = Arena.Create(seed, Room);
        game.Player.Hp = game.Player.MaxHp = 5000;
        TestGames.ClearMonsters(game);
        return game;
    }

    private static Loc East(GameSession game, string trapId, bool visible = true)
    {
        var at = game.Player.Position + new Loc(1, 0);
        game.Level[at].Trap = game.Data.Traps.Single(t => t.Id == trapId).Index;
        if (visible) game.Level[at].Flags |= SquareFlags.TrapVisible;
        return at;
    }

    private static List<string> Messages(GameSession game)
    {
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return said;
    }

    [Fact]
    public void All_of_4_2_5s_traps_are_here()
    {
        var traps = TestData.Game.Traps;
        Assert.Equal(35, traps.Count(t => t.IsTrap));
        Assert.Contains(traps, t => t.Id == "rune_of_necromancy" && t.IsRune && t.Has("ONETIME"));
        Assert.Contains(traps, t => t.Id == "block_fall_trap" && t.Has("DELAY"));
    }

    [Fact]
    public void Traps_are_picked_by_rarity_within_their_depth()
    {
        var rng = new GameRandom(5);
        var picks = Enumerable.Range(0, 3000).Select(_ => TestData.Game.PickTrap(rng, 1, trapDoors: true)!).ToList();
        Assert.All(picks, t => Assert.True(t.MinDepth <= 1, t.Id));
        Assert.Contains(picks, t => t.Id == "alarm");
        Assert.Null(TestData.Game.PickTrap(rng, 0, trapDoors: true)); // none in the town
        var deep = Enumerable.Range(0, 3000).Select(_ => TestData.Game.PickTrap(rng, 50, trapDoors: false)!).ToList();
        Assert.DoesNotContain(deep, t => t.IsTrapDoor);
        Assert.Contains(deep, t => t.Id == "brain_smashing_trap");
    }

    [Fact]
    public void A_trap_is_seen_only_by_a_good_enough_searcher()
    {
        var game = Game();
        var at = East(game, "pit", visible: false);
        game.Level[at].TrapPower = 90;
        game.Player.SkillSearch = 20;
        game.UpdateView();
        Assert.False(game.Level[at].Has(SquareFlags.TrapVisible));

        var said = Messages(game);
        game.Player.SkillSearch = 90;
        game.Known.Forget(at);
        game.UpdateView();
        Assert.True(game.Level[at].Has(SquareFlags.TrapVisible));
        Assert.Contains("You have found a trap.", said);
        Assert.Contains($"Search  {game.SearchSkill,4}", Angband.Core.Records.CharacterDump.Build(game));
    }

    [Fact]
    public void Feather_falling_floats_you_into_a_pit_unhurt()
    {
        var game = Game();
        var ring = game.Player.Inventory.Add(game.Objects.Create("ring_of_feather_falling"))!;
        game.Execute(new WieldCommand(ring));
        East(game, "pit");
        var said = Messages(game);
        var hp = game.Player.Hp;
        game.Execute(new JumpCommand(Direction.East));
        Assert.Contains("You float gently to the bottom of the pit.", said);
        Assert.Equal(hp, game.Player.Hp);
    }

    [Fact]
    public void A_good_saving_throw_shrugs_off_a_mind_blast()
    {
        var game = Game();
        game.Player.SkillSave = 1000;
        East(game, "mind_blasting_trap");
        var said = Messages(game);
        game.Execute(new JumpCommand(Direction.East));
        Assert.Contains("You shake your head and are back to normal.", said);
        Assert.False(game.Player.Timed.Has("confused"));
    }

    [Fact]
    public void Nothing_happens_to_the_trap_immune()
    {
        var game = Game();
        game.Shapechange("eagle"); // TRAP_IMMUNE
        East(game, "knife_trap", visible: false);
        var hp = game.Player.Hp;
        game.Execute(new JumpCommand(Direction.East));
        Assert.Equal(hp, game.Player.Hp);
        Assert.False(game.Player.Timed.Has("cut"));
        Assert.True(game.Level[game.Player.Position].Has(SquareFlags.TrapVisible));
    }

    [Fact]
    public void The_block_fall_goes_off_as_you_leave()
    {
        var game = Game();
        var at = East(game, "block_fall_trap");
        game.Execute(new JumpCommand(Direction.East));
        Assert.Equal(at, game.Player.Position);
        Assert.False(game.Level.FeatureAt(at).Has(TerrainFlags.Wall)); // nothing yet
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(game.Data.Terrain.Ids.Granite, game.Level[at].Feature);
    }

    [Fact]
    public void A_rune_of_summoning_calls_monsters_and_is_gone()
    {
        var game = Game();
        var at = East(game, "summon_rune");
        game.Execute(new JumpCommand(Direction.East));
        Assert.NotEmpty(game.Level.Monsters.All);
        Assert.Equal(0, game.Level[at].Trap); // ONETIME
    }

    [Fact]
    public void A_mana_drain_trap_takes_your_mana()
    {
        var game = Game();
        game.Player.MaxMana = game.Player.Mana = 50;
        East(game, "mana_drain_trap");
        game.Execute(new JumpCommand(Direction.East));
        Assert.True(game.Player.Mana < 50);
    }

    [Fact]
    public void Runes_are_disarmed_with_the_magical_skill()
    {
        var game = Game();
        game.Player.DisarmSkill = 0;
        game.Player.DisarmMagicSkill = 1000;
        var at = East(game, "teleport_rune");
        var said = Messages(game);
        game.Execute(new DisarmCommand(Direction.East));
        Assert.Contains("You have disarmed the strange rune.", said);
        Assert.Equal(0, game.Level[at].Trap);
    }
}
