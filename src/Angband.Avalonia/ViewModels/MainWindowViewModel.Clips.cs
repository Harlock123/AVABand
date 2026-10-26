using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.World;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// A game being played back to make a clip from: the steps to film (the last ones, from where they
/// came to the level they fell on), the map to film them on, and the words to put under it.
/// </summary>
public sealed class ClipJob
{
    private readonly int _from;
    private readonly List<string> _said = [];

    public ClipJob(ReplayPlayer player, ClipMapSource map, int from, string caption, string ending)
    {
        Player = player;
        Map = map;
        _from = from;
        Caption = caption;
        Ending = ending;
        player.Game.Events.Subscribe<MessageEvent>(m => _said.Add(m.Text));
    }

    public ReplayPlayer Player { get; }
    public ClipMapSource Map { get; }
    /// <summary>"Name the Race Class — slain by X at N ft", along the foot of every frame.</summary>
    public string Caption { get; }
    /// <summary>What is written over the last frame, held a while ("You die.").</summary>
    public string Ending { get; }

    public bool UseTiles { get; init; }
    public TilesetManifest? Tileset { get; init; }
    public double FontSize { get; init; } = 13;

    // The level being filmed, and whether a step has just gone to another (the clip then starts again there).
    private int _depth;
    private bool _arrived;

    /// <summary>Plays the game on to where the clip begins (not on the UI thread: it is the whole game, quickly).</summary>
    public void FastForward()
    {
        while (Player.Position < _from && !Player.Done) Player.Step();
        _depth = Player.Game.Player.Depth;
        _said.Clear();
    }

    /// <summary>The next step; false at the end of the game.</summary>
    public bool Step()
    {
        if (!Player.Step()) return false;
        if (Player.Game.Player.Depth != _depth)
        {
            _depth = Player.Game.Player.Depth;
            _arrived = true;
        }
        return true;
    }

    /// <summary>True once after a step took them to another level: the clip starts again from there.</summary>
    public bool TakeArrival()
    {
        var arrived = _arrived;
        _arrived = false;
        return arrived;
    }

    /// <summary>What the game said in the last step (the newest last), or "" if nothing.</summary>
    public string TakeMessages()
    {
        var text = string.Join("  ", _said.TakeLast(2));
        _said.Clear();
        return text;
    }
}

/// <summary>
/// The map of a game being played back for a clip, as the main window draws a game (what the
/// character sees and remembers, with light and shadow) less what only matters at the keyboard:
/// no cursors, paths or pins, and the torchlight holds still, so frames change only where something
/// happened and the GIF stays small.
/// </summary>
public sealed class ClipMapSource(GameSession game, MapCellBuilder cells, bool lightAndShadow) : IMapSource
{
    public int Width => game.Level.Width;
    public int Height => game.Level.Height;
    public Loc Focus => game.Player.Position;
    public bool LightAndShadow => lightAndShadow;

    public MapCell GetCell(int x, int y)
    {
        var p = new Loc(x, y);
        var level = game.Level;
        var terrain = TerrainCell(p);
        if (game.Player.Position == p) return cells.Player(terrain, game.Player.Hp, game.Player.MaxHp);
        if (level.Monsters.At(p) is { } monster && (monster.IsVisible || monster.IsDetected))
            return monster.IsAlly && game.Hireling is { } hireling ? cells.Ally(monster.Race, GameSession.HirelingPicture(hireling), terrain)
                : cells.Monster(monster.Race, terrain, MapCellBuilder.HasRareLook(monster.Id, level.Seed));
        if (terrain.IsUnknown) return terrain;
        ref var sq = ref level[x, y];
        if (sq.Has(SquareFlags.Seen))
        {
            var pile = level.Objects.At(p).Where(i => !game.IsIgnored(i)).ToList();
            if (pile.Count > 0) return cells.Object(pile[0], pile.Count, game.Knowledge, terrain);
            if (game.ObjectShownAt(p) is { } disguise && !game.IsIgnored(disguise)) return cells.Object(disguise, 1, game.Knowledge, terrain);
        }
        else if (game.Known.RememberedObject(p) is { } remembered && !game.IsIgnored(remembered))
            return cells.Object(remembered, 1, game.Knowledge, terrain);
        if (sq.Trap != 0 && sq.Has(SquareFlags.TrapVisible) && game.Data.TrapByIndex(sq.Trap) is { } trap) return cells.Trap(trap, terrain);
        return terrain;
    }

    private MapCell TerrainCell(Loc p)
    {
        var level = game.Level;
        ref var sq = ref level[p];
        var seen = sq.Has(SquareFlags.Seen);
        if (!seen && !game.Known.IsKnown(p)) return MapCell.Unknown;
        var feature = level.Terrain[seen ? sq.Feature : game.Known.Feature(p)];
        if (feature.Mimic is { } mimic) feature = level.Terrain[mimic];
        var lighting = !seen ? TileLighting.Dark
            : !sq.Has(SquareFlags.Glow) && sq.Has(SquareFlags.Lit) ? TileLighting.Torch
            : TileLighting.Lit;
        return cells.Terrain(feature, lighting);
    }

    /// <summary>As the main window shades the map, but steady (no flicker, so no warmth that wavers).</summary>
    public MapShade ShadeAt(int x, int y)
    {
        ref var sq = ref game.Level[x, y];
        if (!sq.Has(SquareFlags.Seen)) return game.Known.IsKnown(new Loc(x, y)) ? new MapShade(0.38f) : default;
        if (sq.Has(SquareFlags.Glow) || !sq.Has(SquareFlags.Lit)) return new MapShade(0.04f);
        var radius = Math.Max(1, game.Player.LightRadius);
        var near = Math.Clamp(new Loc(x, y).DistanceTo(game.Player.Position) / (radius + 0.75), 0, 1);
        return new MapShade((float)(0.08 + 0.5 * near * near));
    }
}

// Clips (AVABand's own): "Save a clip" makes an animated GIF of a character's last moments, to share —
// their game's replay played back headlessly over its last steps (from where they came to the level
// they fell on), a frame a step, in the tiles or letters the map is shown in now, with a line of who
// they were along the foot and "You die." held at the end. Saved in clips/ beside the records (as
// cards are in cards/) and opened in the picture viewer.
public sealed partial class MainWindowViewModel
{
    /// <summary>The most steps a clip shows (fewer if they came to the level they fell on later).</summary>
    public const int ClipSteps = 80;

    /// <summary>Raised with a clip to make and where to save it, and who to tell how it went (the view does the drawing).</summary>
    public event Action<ClipJob, string, Action<string>?>? ClipRequested;

    /// <summary>Where clips are saved: clips/ beside the records (null without them, as in tests).</summary>
    public string? ClipDirectory => _records is null ? null : Path.Combine(_records.Directory, "clips");

    /// <summary>
    /// Makes a clip of the end of a recorded game. <paramref name="won"/>: they retired victorious (the
    /// replay can't say). <paramref name="note"/> hears how it went (as the main window's messages do).
    /// </summary>
    public bool SaveClip(string replayPath, bool won = false, Action<string>? note = null)
    {
        void Say(string text)
        {
            AddMessage(text);
            note?.Invoke(text);
        }
        if (ClipDirectory is not { } folder)
        {
            Say("Clips are saved beside your records; there are none here.");
            return false;
        }
        ClipJob job;
        try
        {
            var file = ReplayFile.Read(replayPath);
            var player = new ReplayPlayer(_data, file);
            var depth = file.End?.Depth ?? player.Game.Player.Depth;
            var where = depth == 0 ? "in the town" : $"at {depth * 50} ft";
            var who = $"{file.Name} the {file.Race} {file.Class}";
            var caption = won ? $"{who} — retired victorious" : file.KilledBy is { } killer ? $"{who} — slain by {killer} {where}" : $"{who} — {where}";
            var map = new ClipMapSource(player.Game, _cells, OptionValue(DisplayOptions.LightAndShadow));
            job = new ClipJob(player, map, Math.Max(0, file.Steps.Count - ClipSteps), caption,
                won ? "Retired, victorious." : file.KilledBy is not null ? "You die." : "")
            {
                UseTiles = UseTiles && SelectedTileset is not null,
                Tileset = SelectedTileset,
            };
        }
        catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException or FormatException)
        {
            Say($"Could not open the replay: {ex.Message}");
            return false;
        }
        var safe = string.Concat(job.Player.File.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var stem = $"{(safe.Length == 0 ? "character" : safe)}-last-moments-{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = Path.Combine(folder, stem + ".gif");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem}-{n}.gif");
        Say("Making a clip of their last moments…");
        ClipRequested?.Invoke(job, path, note);
        return true;
    }

    /// <summary>The view made the clip (or couldn't: the reason in brackets): say where.</summary>
    public void ClipSaved(string pathOrWhy, Action<string>? note = null)
    {
        var text = $"Clip saved: {pathOrWhy}";
        AddMessage(text);
        note?.Invoke(text);
    }
}
