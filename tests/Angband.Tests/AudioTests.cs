using Angband.Audio;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Records what the director asks the engine to do.</summary>
internal sealed class RecordingAudioEngine : NullAudioEngine
{
    public List<string> Effects { get; } = [];
    public List<string> MusicStarted { get; } = [];
    public override bool IsAvailable => true;
    public override void PlayEffect(string path, float gain = 1f) => Effects.Add(Path.GetFileName(path));
    public override void PlayMusic(string path, bool loop = true)
    {
        base.PlayMusic(path, loop);
        MusicStarted.Add(Path.GetFileName(path));
    }
}

public class AudioTests
{
    /// <summary>The bundled packs live in the UI project; find them from the repository root.</summary>
    private static string PacksRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AVABand.sln"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Angband.Avalonia", "soundpacks");
        }
    }

    private static SoundPack Effects => SoundPack.Load(Path.Combine(PacksRoot, "angband-dubtrain"));
    private static SoundPack Music => SoundPack.Load(Path.Combine(PacksRoot, "cc0-dungeon-music"));

    private static (GameSession Game, SoundDirector Director, List<string> Played, RecordingAudioEngine Engine) Setup(GameSession game)
    {
        var engine = new RecordingAudioEngine();
        var director = new SoundDirector(engine) { Effects = Effects, MusicPack = Music };
        var played = new List<string>();
        director.Played += played.Add;
        director.Attach(game);
        return (game, director, played, engine);
    }

    // --- Decoding ---------------------------------------------------------------------------------

    [Fact]
    public void Wav_DecodesSixteenBitPcm()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("avaband-wav-").FullName, "tone.wav");
        short[] samples = [0, 1000, -1000, 32767, -32768];
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(samples.Length * 2);
            foreach (var s in samples) w.Write(s);
        }

        var (pcm, channels, rate) = AudioDecoders.DecodeAll(path);

        Assert.Equal(samples, pcm);
        Assert.Equal(1, channels);
        Assert.Equal(22050, rate);
    }

    [Fact]
    public void BundledOggAndMp3_Decode()
    {
        var mp3 = Effects.Resolve(Effects.Sounds["HIT"][0]);
        var (pcm, channels, rate) = AudioDecoders.DecodeAll(mp3);
        Assert.True(pcm.Length > 1000);
        Assert.InRange(channels, 1, 2);
        Assert.InRange(rate, 8000, 96000);

        using var ogg = AudioDecoders.Open(Music.Resolve(Music.Music["dungeon"][0]));
        var buffer = new short[8192];
        Assert.Equal(buffer.Length, ogg.Read(buffer));
        Assert.Contains(buffer, s => s != 0);
        ogg.Rewind();
        Assert.Equal(buffer.Length, ogg.Read(buffer));
    }

    // --- Packs -----------------------------------------------------------------------------------

    [Fact]
    public void BundledPacks_LoadCleanly()
    {
        var problems = new List<string>();
        var packs = SoundPackCatalog.Discover([PacksRoot], problems);
        Assert.Empty(problems);
        Assert.Equal(["angband-dubtrain", "cc0-dungeon-music"], packs.Select(p => p.Id).Order());
        Assert.True(Effects.HasSounds && !Effects.HasMusic);
        Assert.True(Music.HasMusic && !Music.HasSounds);
        Assert.All(["town", "dungeon", "deep"], mood => Assert.True(Music.Music.ContainsKey(mood)));
    }

    [Fact]
    public void Pack_ReportsMissingAndBadFiles()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-pack-").FullName;
        File.WriteAllText(Path.Combine(dir, SoundPack.FileName), """
            { "name": "Broken", "sounds": { "HIT": ["nope.ogg", "../escape.wav", "notes.txt"] } }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => SoundPack.Load(dir));
        Assert.Contains("missing file 'nope.ogg'", ex.Message);
        Assert.Contains("relative path", ex.Message);
        Assert.Contains("not .wav, .ogg or .mp3", ex.Message);
    }

    // --- Director: effects ----------------------------------------------------------------------

    [Fact]
    public void Combat_PlaysHitMissAndKillSounds()
    {
        var (game, _, played, _) = Setup(Arena.Create());
        game.Player.Hp = game.Player.MaxHp = 1000;
        var mold = Arena.AddMonster(game, "grey_mold", new Loc(6, 2));
        for (var i = 0; i < 40 && mold.IsActive; i++) game.Execute(new WalkCommand(Direction.East));

        Assert.Contains("HIT", played);
        Assert.Contains("KILL", played);
        Assert.Contains("MON_HIT", played); // grey molds "hit"
    }

    [Fact]
    public void MonsterBites_UseTheirOwnSound()
    {
        var (game, _, played, _) = Setup(Arena.Create());
        game.Player.Hp = game.Player.MaxHp = 1000;
        Arena.AddMonster(game, "jackal", new Loc(6, 2));
        for (var i = 0; i < 20 && !played.Contains("MON_BITE"); i++) game.Execute(new HoldCommand());
        Assert.Contains("MON_BITE", played);
    }

    [Fact]
    public void Doors_Stairs_Items_Levels_AndStatus_AllMakeSounds()
    {
        var (game, _, played, _) = Setup(Arena.Create(1,
            "#########",
            "#,@+,,,,#",
            "#########"));
        game.Execute(new WalkCommand(Direction.East));              // bump-open the door
        game.Execute(new CloseCommand(Direction.East));
        var potion = game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        game.Execute(new UseCommand(potion));
        game.Execute(new DropCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil")));
        game.IncreaseTimed(TimedIds.Poisoned, 3);
        game.GainExperience(100);

        foreach (var expected in new[] { "OPENDOOR", "SHUTDOOR", "QUAFF", "DROP", "POISONED", "LEVEL" })
            Assert.Contains(expected, played);

        for (var i = 0; i < 5; i++) game.Execute(new HoldCommand());
        Assert.Contains("RECOVER", played); // the poison wore off

        var town = GameSession.NewGame(TestData.Game, 5);
        var (_, _, stairsPlayed, _) = Setup(town);
        town.Player.Position = town.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        town.Execute(new TakeStairsCommand(true));
        Assert.Contains("STAIRS_DOWN", stairsPlayed);
    }

    [Fact]
    public void LowHitPoints_WarnOnce()
    {
        var (game, _, played, _) = Setup(Arena.Create());
        game.Player.MaxHp = 100;
        game.Player.Hp = 100;
        game.TakeHit(75, "test");
        game.TakeHit(1, "test");
        Assert.Equal(1, played.Count(p => p == "HITPOINT_WARN"));
    }

    [Fact]
    public void DisabledEffects_AreSilent()
    {
        var (game, director, played, engine) = Setup(Arena.Create());
        director.EffectsEnabled = false;
        game.GainExperience(100);
        Assert.Empty(played);
        Assert.Empty(engine.Effects);
    }

    [Fact]
    public void AmbientSounds_DependOnDepth()
    {
        Assert.Equal("AMBIENT_DAY", SoundDirector.AmbientSound(0, day: true));
        Assert.Equal("AMBIENT_NITE", SoundDirector.AmbientSound(0, day: false));
        Assert.Equal("AMBIENT_DNG1", SoundDirector.AmbientSound(5, true));
        Assert.Equal("AMBIENT_DNG3", SoundDirector.AmbientSound(45, true));
        Assert.Equal("AMBIENT_DNG5", SoundDirector.AmbientSound(120, true));
    }

    // --- Director: music --------------------------------------------------------------------------

    [Fact]
    public void Music_FollowsThePlayer()
    {
        var game = GameSession.NewGame(TestData.Game, 7);
        var (_, director, _, engine) = Setup(game);
        Assert.Equal("town", director.MusicMood);
        Assert.Equal("The_Old_Tower_Inn.mp3", engine.MusicStarted.Last());

        game.Execute(new DebugJumpCommand(5));
        Assert.Equal("dungeon", director.MusicMood);
        Assert.Contains(engine.MusicStarted.Last(), Music.Music["dungeon"]);

        var tracks = engine.MusicStarted.Count;
        game.Execute(new DebugJumpCommand(6)); // same mood: keep playing
        Assert.Equal(tracks, engine.MusicStarted.Count);

        game.Execute(new DebugJumpCommand(30));
        Assert.Equal("deep", director.MusicMood);
        Assert.Equal("dark_cavern_ambient_002.ogg", engine.MusicStarted.Last());

        director.MusicEnabled = false;
        Assert.Null(engine.CurrentMusic);
    }

    [Fact]
    public void Sound_NeverChangesTheGame()
    {
        static string Play(bool withSound)
        {
            var game = GameSession.NewGame(TestData.Game, 77);
            if (withSound) Setup(game);
            game.Execute(new DebugJumpCommand(8));
            game.Player.Hp = game.Player.MaxHp = 100_000;
            for (var i = 0; i < 300; i++) game.Execute(new HoldCommand());
            return $"{game.Player.Hp} {game.GameTurn} " +
                   string.Join(";", game.Level.Monsters.All.Select(m => $"{m.Race.Id}@{m.Position}"));
        }
        Assert.Equal(Play(false), Play(true));
    }

    // --- Real device -----------------------------------------------------------------------------

    [Fact]
    public void OpenAl_OpensOrFallsBackToSilence()
    {
        using var engine = OpenAlAudioEngine.CreateOrSilent();
        // Either way these must not throw.
        engine.SetVolumes(0.01f, 0.01f, 0.01f);
        engine.PlayEffect(Effects.Resolve(Effects.Sounds["HIT"][0]));
        engine.PlayMusic(Music.Resolve(Music.Music["town"][0]));
        Thread.Sleep(300);
        engine.StopMusic();
        if (engine.IsAvailable) Assert.NotNull(engine);
    }

    [Fact]
    public void Getting_hungrier_plays_the_hunger_sound()
    {
        var (game, _, played, _) = Setup(Arena.Create(3));
        game.Player.Food = game.Data.Constants.FoodHungry + 1;
        game.SetFood(game.Data.Constants.FoodHungry - 1);
        Assert.Contains("HUNGRY", played);
        played.Clear();
        game.SetFood(game.Data.Constants.FoodFull - 1); // eating back up is quiet
        Assert.DoesNotContain("HUNGRY", played);
    }
}
