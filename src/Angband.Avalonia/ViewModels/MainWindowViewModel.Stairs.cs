using Angband.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The scene shown for a moment on taking a staircase: down or up, the new depth, a caption, and
/// (for the town) whether it is day. <see cref="Picture"/> is the player's own picture, if they have
/// put one in the art folder; otherwise the scene is painted.
/// </summary>
public sealed record StairScene(bool Down, int Depth, string Caption, bool Day, string? Picture);

// Stair scenes (AVABand's own; the option "Show a scene on taking the stairs", on by default): going
// down, a stairwell falling away into torchlit dark; going up, steps climbing toward a pale light —
// daylight or moonlight when the town is above. They last a moment, and any key or click ends them
// early (and still does what it does). Only the stairs show them: not trapdoors, recall or debug jumps.
// stairs-down.png / stairs-up.png in the art folder (beside the saves) replace the painted scenes.
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStairScene))]
    private StairScene? _stairScene;

    public bool HasStairScene => StairScene is not null;

    /// <summary>Where the player's own stair pictures go: &lt;AppData&gt;/AVABand/art.</summary>
    public string ArtDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "art");

    /// <summary>After a command: if it took the stairs to another level, the scene for it.</summary>
    private void ShowStairScene(GameCommand command, int fromDepth)
    {
        var depth = _game.Player.Depth;
        if (command is not TakeStairsCommand || depth == fromDepth || _game.Player.IsDead || !OptionValue(DisplayOptions.StairScenes)) return;
        var down = depth > fromDepth;
        var caption = down ? $"Descending… {depth * 50} ft (level {depth})"
            : depth == 0 ? $"Up into the town, by {(_game.IsDaytime ? "day" : "night")}"
            : $"Climbing… {depth * 50} ft (level {depth})";
        var picture = Path.Combine(ArtDirectory, down ? "stairs-down.png" : "stairs-up.png");
        StairScene = new StairScene(down, depth, caption, _game.IsDaytime, File.Exists(picture) ? picture : null);
    }

    /// <summary>The scene has played (or a key or click cut it short).</summary>
    public void EndStairScene() => StairScene = null;
}
