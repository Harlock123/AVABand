using Angband.Core.Definitions;
using Angband.Core.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Screen reader support (AVABand's own; the option "Screen reader support", off by default). The map
// is drawn, so a screen reader can't read it: instead a live region announces what is said — each
// message, each prompt with its choices, each tip — gathered over a turn and spoken together, and
// Ctrl+Shift+D ("Describe surroundings") reads out your hit points, where you are, the monsters and
// objects in view with where they are (and which monsters are a danger to you), and the nearest stairs.
// The map, the status line, the hotbar slots, the item rows, the weight and slots line (saying when
// the pack is full, and said as it fills), the shop's rows (each with its note on whether it would
// suit you, said as the arrows reach it) and its Services button carry accessible names; going into
// a shop says where you are, what's for sale and the service on offer. (Avalonia 12 speaks to screen readers on Windows,
// macOS and Linux — AT-SPI there, when the desktop's accessibility is on.)
public sealed partial class MainWindowViewModel
{
    /// <summary>What the live region says; it changes each time there is something new to say.</summary>
    [ObservableProperty] private string _announcement = "";

    /// <summary>The map described in words (its accessible name), kept current while the option is on.</summary>
    [ObservableProperty] private string _mapSummary = "The map.";

    private readonly List<string> _speech = [];
    private bool _speechPosted;
    private bool _speechToggle;

    private bool ScreenReaderOn => OptionValue(DisplayOptions.ScreenReader);

    /// <summary>Queues something to be said; everything queued in one go is said together.</summary>
    private void Announce(string text)
    {
        if (!ScreenReaderOn || string.IsNullOrWhiteSpace(text)) return;
        if (_speech.Count == 0 || _speech[^1] != text) _speech.Add(text);
        if (_speechPosted) return;
        _speechPosted = true;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(FlushSpeech, global::Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Says what was queued (a changed text, so the same words twice are said twice).</summary>
    public void FlushSpeech()
    {
        _speechPosted = false;
        if (_speech.Count == 0) return;
        _speechToggle = !_speechToggle;
        Announcement = string.Join(" ", _speech) + (_speechToggle ? "\u200B" : ""); // (a zero-width space)
        _speech.Clear();
    }

    partial void OnLastMessageChanged(string value)
    {
        Announce(value);
        UpdateMessageLines();
    }

    partial void OnHintTextChanged(string value)
    {
        if (value.Length > 0) Announce("Tip: " + value);
    }

    /// <summary>A prompt has opened: says its question and every choice.</summary>
    private void AnnouncePrompt()
    {
        if (!ScreenReaderOn) return;
        var lines = ChoiceRows.Select(r => $"{r.Letter}, {r.Text}")
            .Concat(SpellPromptRows.Select(r => $"{r.Letter}, {r.Name}, {r.Stats}"))
            .Concat(PromptRows.Select(r => $"{r.Letter}, {r.Name}"));
        Announce($"{PromptTitle} {string.Join(". ", lines)}.");
    }

    /// <summary>Ctrl+Shift+D: says what is around you.</summary>
    [RelayCommand]
    public void DescribeSurroundings()
    {
        MapSummary = BuildMapSummary();
        if (ScreenReaderOn) Announce(MapSummary);
        else AddMessage(MapSummary);
    }

    /// <summary>Keeps the map's accessible name current (with each refresh, while the option is on).</summary>
    private void RefreshMapSummary()
    {
        if (ScreenReaderOn) MapSummary = BuildMapSummary();
    }

    /// <summary>"Hit points 18 of 20. 250 feet, level 5. You see: a cave spider, 3 north 2 east. ..."</summary>
    public string BuildMapSummary()
    {
        var p = _game.Player;
        var parts = new List<string>
        {
            $"Hit points {p.Hp} of {p.MaxHp}.",
            p.Depth == 0 ? $"In the town, by {(_game.IsDaytime ? "day" : "night")}." : $"{p.Depth * 50} feet, level {p.Depth}.",
        };
        string List(IEnumerable<Angband.Core.Game.VisibleListRow> rows) =>
            string.Join("; ", rows.Where(r => !r.IsHeading).Select(r => r.Location.Length > 0 ? $"{r.Text.Trim()}, {Spoken(r.Location)}" : r.Text.Trim()));
        // (Each monster your knowledge of says is a danger to you says so: "could kill you", "dangerous".)
        var monsters = List(_game.MonsterList().Select(r => r.TileKey?.StartsWith("monster:", StringComparison.Ordinal) == true
            && _game.Data.Monster(r.TileKey["monster:".Length..]) is { } race && _game.DangerWord(race) is { } danger
                ? r with { Text = $"{r.Text.Trim()} ({danger})" } : r));
        parts.Add(monsters.Length > 0 ? $"Monsters: {monsters}." : "No monsters in view.");
        var objects = List(_game.ObjectList());
        if (objects.Length > 0) parts.Add($"Objects: {objects}.");
        foreach (var (flag, name) in new[] { (TerrainFlags.DownStair, "down"), (TerrainFlags.UpStair, "up") })
        {
            var stairs = _game.Level.AllLocs().Where(l => _game.Known.IsKnown(l) && _game.Level.Has(l, flag))
                .OrderBy(l => l.DistanceTo(p.Position)).FirstOrDefault(new Loc(-1, -1));
            if (stairs.X >= 0) parts.Add($"Nearest stairs {name}: {(stairs == p.Position ? "here" : Offset(stairs - p.Position))}.");
        }
        return string.Join(" ", parts);
    }

    /// <summary>"3 N 2 E" as it would be said: "3 north 2 east".</summary>
    private static string Spoken(string location) => string.Join(" ", location.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(w => w switch { "N" => "north", "S" => "south", "E" => "east", "W" => "west", _ => w }));

    private static string Offset(Loc d)
    {
        var said = new List<string>();
        if (d.Y != 0) said.Add($"{Math.Abs(d.Y)} {(d.Y < 0 ? "north" : "south")}");
        if (d.X != 0) said.Add($"{Math.Abs(d.X)} {(d.X < 0 ? "west" : "east")}");
        return string.Join(" ", said);
    }
}
