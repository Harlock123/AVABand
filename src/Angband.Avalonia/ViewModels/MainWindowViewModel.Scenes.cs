using Angband.Core.Game;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>The moments that show a scene.</summary>
public enum SceneKind { StairsDown, StairsUp, RecallUp, RecallDown, Cavern, Labyrinth, Fortress, Moria, Lair, Gauntlet, Danger, Unique, Death }

/// <summary>
/// A scene shown for a moment (AVABand's own): its kind, caption, the depth, whether the town is in
/// daylight, and the picture to show — the player's own, or the bundled artwork — or none, when the
/// scene is painted. A unique's scene carries its glyph and colour for the painted version.
/// </summary>
/// With <see cref="WaitForKey"/> (the option "Scenes stay until you press Space") it holds, once
/// it has faded in, until dismissed.
public sealed record AmbientScene(SceneKind Kind, string Caption, int Depth, bool Day, string? Picture,
    string? Glyph = null, uint GlyphColor = 0xFFFFFFFF, string? Subtitle = null, bool WaitForKey = false);

// Scenes (the option "Show scenes at moments of note", on by default): taking the stairs, Word of
// Recall, arriving in a cavern, labyrinth or fortress, a level whose feeling is deadly, meeting a
// unique for the very first time, and death. They queue (the stairs, then the cavern you arrive in),
// each lasting a moment; a click ends one, a key ends them all (and still does what it does). With
// "Scenes stay until you press Space" each holds instead: Space, Enter, a click or the controller's A
// moves on to the next, Escape (or B) ends them all, and other keys do nothing meanwhile.
// Pictures are looked for in the player's art folder (<AppData>/AVABand/art), then among the bundled
// artwork (unless "Use the bundled pictures" is off); with none, the scene is painted.
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScene))]
    private AmbientScene? _scene;

    public bool HasScene => Scene is not null;

    private readonly Queue<AmbientScene> _sceneQueue = new();
    private readonly List<AmbientScene> _pendingScenes = [];

    /// <summary>The player's own pictures: &lt;AppData&gt;/AVABand/art.</summary>
    public string ArtDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "art");

    /// <summary>The artwork that comes with the game (art/ beside the program).</summary>
    public string BundledArtDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "art");

    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>The picture for a scene: the first name found, in the player's folder, then the bundled one.</summary>
    public string? PictureFor(params string[] names)
    {
        var folders = OptionValue(DisplayOptions.ScenePictures) ? new[] { ArtDirectory, BundledArtDirectory } : [ArtDirectory];
        foreach (var name in names)
            foreach (var folder in folders)
                foreach (var ext in PictureExtensions)
                    if (Path.Combine(folder, name + ext) is var path && File.Exists(path)) return path;
        return null;
    }

    private bool ScenesOn => OptionValue(DisplayOptions.Scenes);

    /// <summary>Queues a scene to show once the command in hand is done.</summary>
    private void AddScene(SceneKind kind, string caption, string? glyph = null, uint glyphColor = 0xFFFFFFFF, string? subtitle = null,
        params string[] pictures)
    {
        if (!ScenesOn) return;
        var depth = _game.Player.Depth;
        _pendingScenes.Add(new AmbientScene(kind, caption, depth, _game.IsDaytime, PictureFor(pictures), glyph, glyphColor, subtitle,
            OptionValue(DisplayOptions.ScenesWait)));
    }

    /// <summary>After a command: the stairs taken (if any), then whatever else happened, in order of note.</summary>
    private void ShowScenes(GameCommand command, int fromDepth)
    {
        var depth = _game.Player.Depth;
        if (command is TakeStairsCommand && depth != fromDepth && !_game.Player.IsDead)
        {
            var down = depth > fromDepth;
            var caption = down ? $"Descending… {depth * 50} ft (level {depth})"
                : depth == 0 ? $"Up into the town, by {(_game.IsDaytime ? "day" : "night")}"
                : $"Climbing… {depth * 50} ft (level {depth})";
            AddScene(down ? SceneKind.StairsDown : SceneKind.StairsUp, caption, pictures: down ? ["stairs-down"]
                : depth == 0 ? ["stairs-up-town", "stairs-up"] : ["stairs-up"]);
        }
        if (_pendingScenes.Count == 0) return;
        foreach (var scene in _pendingScenes.OrderBy(s => s.Kind)) _sceneQueue.Enqueue(scene);
        _pendingScenes.Clear();
        if (Scene is null) Scene = _sceneQueue.Dequeue();
    }

    /// <summary>The scene has played (or a click cut it short): the next, if any.</summary>
    public void EndScene() => Scene = _sceneQueue.Count > 0 ? _sceneQueue.Dequeue() : null;

    /// <summary>Whether the scene on screen is holding for Space.</summary>
    public bool SceneWaits => Scene?.WaitForKey == true;

    /// <summary>
    /// A controller action (or key) while a scene holds for Space: Confirm moves on, Cancel ends them
    /// all, anything else is kept from the game. False when no scene is holding.
    /// </summary>
    public bool HandleWaitingScene(InputAction action)
    {
        if (!SceneWaits) return false;
        if (action == InputAction.Confirm) EndScene();
        else if (action == InputAction.Cancel) SkipScenes();
        return true;
    }

    /// <summary>A key: every scene waiting goes.</summary>
    public void SkipScenes()
    {
        _sceneQueue.Clear();
        Scene = null;
    }

    // --- What shows a scene ------------------------------------------------------------------------

    private void OnRecalled(RecalledEvent e)
    {
        if (e.Up) AddScene(SceneKind.RecallUp, "Word of Recall draws you up to the town", pictures: ["recall-town"]);
        else AddScene(SceneKind.RecallDown, "Word of Recall draws you down into the depths", pictures: ["recall-dungeon"]);
    }

    /// <summary>
    /// Arriving: each level out of the ordinary, and a deadly feeling, has its scene (a lair, gauntlet
    /// or moria level shows the cavern's or labyrinth's picture until it has its own).
    /// </summary>
    private void SceneForArrival(LevelChangedEvent e)
    {
        var where = $"{e.Depth * 50} ft (level {e.Depth})";
        switch (e.ProfileId)
        {
            case "cavern": AddScene(SceneKind.Cavern, $"A cavern… {where}", pictures: ["level-cavern"]); break;
            case "labyrinth": AddScene(SceneKind.Labyrinth, $"A labyrinth… {where}", pictures: ["level-labyrinth"]); break;
            // 4.2.5's hard centre: a greater vault in the middle of the level, walled round by caverns.
            case "hard_centre": AddScene(SceneKind.Fortress, $"A fortress… {where}", pictures: ["level-fortress"]); break;
            case "moria": AddScene(SceneKind.Moria, $"Old mines… {where}", pictures: ["level-moria", "level-cavern"]); break;
            case "lair": AddScene(SceneKind.Lair, $"A lair… {where}", pictures: ["level-lair", "level-cavern"]); break;
            case "gauntlet": AddScene(SceneKind.Gauntlet, $"A gauntlet… {where}", pictures: ["level-gauntlet", "level-labyrinth"]); break;
        }
        // Angband's monster feelings 1-3: omens of death, murderous, terribly dangerous.
        if (e.Depth > 0 && _game.FeelingStatus is not null && _game.Level.Feeling % 10 is >= 1 and <= 3)
            AddScene(SceneKind.Danger, LevelFeelings.MonsterTexts[_game.Level.Feeling % 10] + ".", pictures: ["danger"]);
    }

    private void OnUniqueFirstSeen(UniqueFirstSeenEvent e)
    {
        var race = e.Monster.Race;
        var name = _game.MonsterName(e.Monster);
        AddScene(SceneKind.Unique, char.ToUpperInvariant(name[0]) + name[1..], race.Glyph.ToString(), _cells.Color(race.Color),
            "You have never met it before.", "unique");
    }

    private void SceneForDeath()
    {
        var p = _game.Player;
        AddScene(SceneKind.Death, $"{p.Name} the {p.Race?.Name} {p.Class?.Name}",
            subtitle: $"Killed by {p.KilledBy} {(p.Depth == 0 ? "in the town" : $"at {p.Depth * 50} ft")}", pictures: ["death"]);
    }
}
