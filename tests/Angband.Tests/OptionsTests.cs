using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Core.Time;

namespace Angband.Tests;

/// <summary>Angband's options (list-options.h): birth, interface and cheat.</summary>
public class OptionsTests
{
    private static GameSession Born(params (string Id, bool Value)[] options)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Options = options.ToDictionary(o => o.Id, o => o.Value) };
        return GameSession.NewGame(TestData.Game, 21, spec);
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static void StandOn(GameSession game, TerrainFlags stair) =>
        game.Player.Position = game.Level.FindFeature(stair).First();

    // --- The option set -------------------------------------------------------------------------

    [Fact]
    public void Options_HaveAngbandsDefaults_AndIgnoreUnknownNames()
    {
        var options = new GameOptions(new Dictionary<string, bool> { ["no_such_option"] = true });
        Assert.True(options[OptionIds.PickupInven]);
        Assert.False(options[OptionIds.PickupAlways]);
        Assert.True(options[OptionIds.StartKit]);
        Assert.False(options.IsSet("no_such_option"));
        Assert.All(OptionCatalog.All, o => Assert.Equal(o.Default, new GameOptions()[o.Id]));
    }

    [Fact]
    public void BirthOptions_AreFixed_AndCheatsMarkTheCharacter()
    {
        var game = Born();
        Assert.False(game.SetOption(OptionIds.NoSelling, false));
        Assert.True(game.NoSelling);

        Assert.False(game.IsCheater);
        Assert.True(game.SetOption(OptionIds.CheatHear, true));
        Assert.True(game.SetOption(OptionIds.CheatHear, false));
        Assert.True(game.IsCheater); // once used, always a cheater
    }

    [Fact]
    public void BirthAndCheatOptions_AreSaved()
    {
        var game = Born((OptionIds.ForceDescend, true), (OptionIds.NoSelling, false));
        game.SetOption(OptionIds.CheatRoom, true);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.True(loaded.ForceDescend);
        Assert.False(loaded.NoSelling);
        Assert.True(loaded.Options[OptionIds.CheatRoom]);
        Assert.Contains(OptionIds.CheatRoom, loaded.CheatsUsed);
    }

    // --- Birth options --------------------------------------------------------------------------

    [Fact]
    public void ConnectedStairs_CanBeTurnedOff()
    {
        var game = Born((OptionIds.ConnectStairs, false));
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.False(game.Level.Has(game.Player.Position, TerrainFlags.Stair));
    }

    [Fact]
    public void ForcedDescent_NeverGoesUp_AndGoesBelowTheDeepestLevel()
    {
        var game = Born((OptionIds.ForceDescend, true));
        var messages = Messages(game);
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(1, game.Player.Depth);

        // Stairs up do nothing...
        game.Level[game.Player.Position].Feature = TestData.Game.Terrain.Ids.UpStair;
        Assert.False(game.Execute(new TakeStairsCommand(Down: false)));
        Assert.Contains("Nothing happens!", messages);

        // ...and the way down leads below the deepest level reached.
        game.Player.MaxDepth = 7;
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(8, game.Player.Depth);

        // Teleport level only ever sinks.
        for (var i = 0; i < 5; i++) game.TeleportPlayerLevel();
        Assert.Equal(13, game.Player.Depth);
    }

    [Fact]
    public void NoRecall_MakesRecallDoNothing()
    {
        var game = Born((OptionIds.NoRecall, true));
        var messages = Messages(game);
        Assert.True(game.ToggleRecall());
        Assert.Equal(0, game.Player.RecallTimer);
        Assert.Contains("Nothing happens.", messages);

        game.Player.IsWinner = true; // Morgoth's slayer may recall again
        game.ToggleRecall();
        Assert.True(game.Player.RecallTimer > 0);
    }

    [Fact]
    public void NoArtifacts_StopsArtifactsBeingMade()
    {
        var game = Born((OptionIds.NoArtifacts, true));
        var dagger = game.Objects.Create("dagger");
        var rng = new Angband.Core.Randomness.GameRandom(1);
        for (var i = 0; i < 1000; i++) Assert.False(game.Objects.TryMakeArtifact(rng, dagger, 100));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ArtifactsLeftBehindUnfound_MayComeBack_UnlessLost(bool loseArts, bool stillCreated)
    {
        var game = Born((OptionIds.LoseArtifacts, loseArts));
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        var phial = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        var far = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && !game.Level[p].Has(Angband.Core.World.SquareFlags.Seen)
                                                  && !game.Known.IsKnown(p));
        game.Level.Objects.Add(far, phial);

        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(stillCreated, game.Objects.CreatedArtifacts.Contains("galadriel"));
    }

    [Fact]
    public void WithoutTheStartingKit_OnlyFoodAndLight_AndTheRestAsGold()
    {
        var with = Born();
        var without = Born((OptionIds.StartKit, false));
        Assert.Equal(2, without.Player.Inventory.All.Count());
        Assert.Contains(without.Player.Inventory.All, i => i.Base.Id == "food" && i.Number == 1);
        Assert.Contains(without.Player.Inventory.All, i => i.Base.Id == "light" && i.Number == 1);
        Assert.Null(without.Player.Inventory.Weapon);
        Assert.True(without.Player.Gold > with.Player.Gold);
    }

    [Fact]
    public void KnowRunesAndFlavors_AtBirth()
    {
        var game = Born((OptionIds.KnowRunes, true), (OptionIds.KnowFlavors, true));
        Assert.All(ObjectInfo.AllRunes(TestData.Game), r => Assert.True(game.Knowledge.KnowsRune(r), r));
        Assert.True(game.Knowledge.IsAware(TestData.Game.Object("speed")!));
        Assert.False(Born().Knowledge.IsAware(TestData.Game.Object("speed")!));
    }

    // --- Interface options ----------------------------------------------------------------------

    [Fact]
    public void Pickup_FollowsTheOptions_AndCostsATenthOfATurnEach()
    {
        var game = Arena.Create(22);
        var east = game.Player.Position + new Loc(1, 0);
        game.Level.Objects.Add(east, game.Objects.Create("main_gauche"));
        game.SetOption(OptionIds.PickupAlways, true);
        var turn = game.GameTurn;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == "main_gauche");
        Assert.True(game.GameTurn - turn > EnergyTable.GameTurnsPerNormalTurn);

        // Matching items: taken with pickup_inven, left with it off.
        game.SetOption(OptionIds.PickupAlways, false);
        game.SetOption(OptionIds.PickupInven, false);
        game.Level.Objects.Add(game.Player.Position + new Loc(1, 0), game.Objects.Create("ration_of_food"));
        game.Execute(new WalkCommand(Direction.East));
        Assert.True(game.Level.Objects.Any(game.Player.Position));
    }

    [Fact]
    public void ShowDamage_AddsTheDamageToHitMessages()
    {
        var game = Arena.Create(23);
        var messages = Messages(game);
        game.SetOption(OptionIds.ShowDamage, true);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 10_000;
        for (var i = 0; i < 20 && !messages.Any(m => m.StartsWith("You hit")); i++) game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(messages, m => System.Text.RegularExpressions.Regex.IsMatch(m, @"^You hit .* \(\d+\)\."));
    }

    [Fact]
    public void UseOldTarget_Off_AimsAtTheNearestMonsterInstead()
    {
        var game = Arena.Create(24);
        var near = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(1, 0));
        var far = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(4, 0));
        game.UpdateView();
        game.SetTarget(far);
        Assert.Equal(far.Position, game.AimPoint());
        game.SetOption(OptionIds.UseOldTarget, false);
        Assert.Equal(near.Position, game.AimPoint());
    }

    [Fact]
    public void ShowFlavors_NamesTheFlavourOfKnownObjects()
    {
        var game = Born();
        var potion = game.Objects.Create("cure_light_wounds");
        var flavor = game.Knowledge.Flavor(potion.Kind)!.Name;
        Assert.DoesNotContain(flavor, game.Describe(potion));
        game.SetOption(OptionIds.ShowFlavors, true);
        Assert.Contains($"{flavor} Potion of Cure Light Wounds", game.Describe(potion));
    }

    [Fact]
    public void Travel_IsntDisturbedByAMonsterThatStaysPut()
    {
        // A sleeping eye in an alcove beside the way, in view the whole time.
        var game = Arena.Create(25, "#############", "#,@,,,,,,,,,#", "###,#########", "#############");
        var eye = Arena.AddMonster(game, "floating_eye", new Loc(3, 2), awake: false);
        game.UpdateView();
        Assert.True(eye.IsVisible);
        var target = game.Player.Position + new Loc(9, 0);
        game.Execute(new TravelCommand(target));
        Assert.Equal(target, game.Player.Position);
    }

    // --- Cheats ---------------------------------------------------------------------------------

    [Fact]
    public void CheatLive_RefusesDeath_AndSendsYouToTown()
    {
        var game = Born();
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        game.SetOption(OptionIds.CheatLive, true);
        game.TakeHit(10_000, "a test");
        Assert.False(game.Player.IsDead);
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
        Assert.Equal(0, game.Player.Depth);
        Assert.True(game.IsCheater);
        Assert.Contains("not scored", CharacterDump.Build(game, []));
    }

    [Fact]
    public void CheatPeeks_ReportTheLevel()
    {
        var game = Born();
        var messages = Messages(game);
        game.SetOption(OptionIds.CheatRoom, true);
        StandOn(game, TerrainFlags.DownStair);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Contains(messages, m => m.StartsWith("Level profile: "));
    }

    [Fact]
    public void TheDump_ListsBirthOptions()
    {
        var dump = CharacterDump.Build(Born((OptionIds.ForceDescend, true)), []);
        Assert.Contains("[Birth options]", dump);
        Assert.Matches(@"Force player descent.*: yes \(birth_force_descend\)", dump);
    }
}
