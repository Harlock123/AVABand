using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>The moments that show a scene.</summary>
/// (Several at once queue in this order: how you got here, where you are, what happened, death last.)
public enum SceneKind
{
    StairsDown, StairsUp, RecallUp, RecallDown, DeepDescent, Trapdoor, Cavern, Labyrinth, Fortress, Moria, Lair, Gauntlet, Danger, Unique,
    BossSlain, QuestComplete, Death,
}

/// <summary>
/// A scene shown for a moment (AVABand's own): its kind, caption, the depth, whether the town is in
/// daylight, and the picture to show — the player's own, or the bundled artwork — or none, when the
/// scene is painted. A unique's scene carries its glyph and colour for the painted version.
/// </summary>
/// A Word of Recall scene with <see cref="PortalArt"/> (the folder of the rent's rim and the hand) is
/// the animated cutscene over its backdrop (<see cref="Picture"/>). With <see cref="WaitForKey"/> (the option "Scenes stay until you press Space") it holds, once
/// it has faded in, until dismissed.
public sealed record AmbientScene(SceneKind Kind, string Caption, int Depth, bool Day, string? Picture,
    string? Glyph = null, uint GlyphColor = 0xFFFFFFFF, string? Subtitle = null, bool WaitForKey = false,
    string? PortalArt = null);

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

    /// <summary>Every scene waiting goes (and with them, any input being dropped for them).</summary>
    public void SkipScenes()
    {
        _sceneQueue.Clear();
        Scene = null;
        _sceneInputDroppedUntil = 0;
    }

    /// <summary>
    /// How long (ms) input is dropped as a scene appears or is cut short: keys typed ahead while the
    /// turn was worked out (they arrive all at once, just after the scene does) and the next few after a
    /// key ends it — so a scene isn't gone before it's seen, and keys meant for before it don't act after.
    /// </summary>
    public const int SceneTypeAheadMs = 250;

    /// <summary>The clock (ms) the scenes' input drop runs on (tests set their own).</summary>
    public Func<long> SceneClock { get; set; } = () => Environment.TickCount64;

    private long _sceneInputDroppedUntil;

    /// <summary>The key that cut the scenes short, while it's still held (its repeats are dropped too).</summary>
    private string? _sceneKeyHeld;

    partial void OnSceneChanged(AmbientScene? value)
    {
        if (value is not null) _sceneInputDroppedUntil = SceneClock() + SceneTypeAheadMs;
    }

    /// <summary>
    /// A key (by name; null for a controller button) before the game sees it: true when the scenes take
    /// it — typed ahead of a scene (dropped), the key that cut them short still held (its repeats
    /// dropped), or the first press while a scene passes, which ends every scene waiting, as Escape
    /// would, and does nothing more. A scene holding for Space has its own keys (HandleWaitingScene).
    /// </summary>
    public bool SceneTakesInput(string? key)
    {
        if (key is not null && key == _sceneKeyHeld) return true;
        if (SceneClock() < _sceneInputDroppedUntil) return true;
        if (!HasScene || SceneWaits) return false;
        SkipScenes();
        _sceneKeyHeld = key;
        _sceneInputDroppedUntil = SceneClock() + SceneTypeAheadMs;
        return true;
    }

    /// <summary>A key let go (or the window left): the key that cut the scenes short works again.</summary>
    public void SceneKeyReleased(string? key)
    {
        if (key is null || key == _sceneKeyHeld) _sceneKeyHeld = null;
    }

    // --- What shows a scene ------------------------------------------------------------------------

    /// <summary>
    /// Word of Recall: a rent torn in the world where you stand — the dungeon room you read it in, or
    /// the town square — and a vast incorporeal hand reaching out of it to take you. Your own
    /// picture (recall-town, recall-dungeon) replaces the cutscene; without the bundled pictures,
    /// the painted rings of light.
    /// </summary>
    private void OnRecalled(RecalledEvent e)
    {
        var (kind, caption, own) = e.Up
            ? (SceneKind.RecallUp, "Word of Recall draws you up to the town", "recall-town")
            : (SceneKind.RecallDown, "Word of Recall draws you down into the depths", "recall-dungeon");
        if (!ScenesOn) return;
        var backdrop = e.Up ? "recall-portal-room.jpg" : _game.IsDaytime ? "recall-portal-square-day.jpg" : "recall-portal-square-night.jpg";
        var bundled = Path.Combine(BundledArtDirectory, backdrop);
        if (PictureFor(own) is null && OptionValue(DisplayOptions.ScenePictures) && File.Exists(bundled))
        {
            _pendingScenes.Add(new AmbientScene(kind, caption, _game.Player.Depth, _game.IsDaytime, bundled,
                WaitForKey: OptionValue(DisplayOptions.ScenesWait), PortalArt: BundledArtDirectory));
            return;
        }
        AddScene(kind, caption, pictures: [own]);
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

    /// <summary>Falling through the floor: a trap door, or Deep Descent's floor opening.</summary>
    private void OnFell(FellEvent e)
    {
        var depth = e.ToDepth;
        if (e.DeepDescent) AddScene(SceneKind.DeepDescent, $"The floor opens beneath you… down to {depth * 50} ft", pictures: ["deep-descent"]);
        else AddScene(SceneKind.Trapdoor, $"You fall through a trap door… {depth * 50} ft (level {depth})", pictures: ["trapdoor"]);
    }

    /// <summary>A great foe: a unique from 500 ft down, or a quest's own (its death has a scene; in view, it has music).</summary>
    public static bool IsGreatFoe(MonsterRaceDef race) => race.IsUnique && (race.Depth >= 10 || race.Has(MonsterFlags.Questor));

    /// <summary>One of AVABand's quests brought to a good end.</summary>
    private void OnAvaQuestCompleted(AvaQuestCompletedEvent e) =>
        AddScene(SceneKind.QuestComplete, $"Quest complete: {e.Name}", subtitle: "Your quest log is on Ctrl+J.", pictures: ["quest-complete"]);

    /// <summary>
    /// A great foe slain: a unique from 500 ft down, or a quest's own (Durgash, Hathol, the Shade,
    /// Sauron, Morgoth) — shallower uniques are many and small, and pass without one.
    /// </summary>
    private void OnMonsterKilledForScene(MonsterKilledEvent e)
    {
        if (!e.IsUnique || _data.Monster(e.RaceId) is not { } race || !IsGreatFoe(race)) return;
        var where = _game.Player.Depth == 0 ? "in the town" : $"at {_game.Player.Depth * 50} ft";
        AddScene(SceneKind.BossSlain, $"{char.ToUpperInvariant(race.Name[0])}{race.Name[1..]} is slain", race.Glyph.ToString(),
            _cells.Color(race.Color), where, "boss-slain");
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
