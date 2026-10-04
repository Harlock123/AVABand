using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Geometry;
using Angband.Core.Time;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>The display-only interface options (Angband list-options.h), handled by the UI.</summary>
public static class DisplayOptions
{
    public const string SolidWalls = "solid_walls";
    public const string HybridWalls = "hybrid_walls";
    public const string YellowLight = "view_yellow_light";
    public const string CenterPlayer = "center_player";
    public const string PurpleUniques = "purple_uniques";
    public const string HpChangesColor = "hp_changes_color";
    public const string MouseMovement = "mouse_movement";
    public const string HighlightPlayer = "highlight_player";
    public const string ShowTarget = "show_target";
    public const string EffectiveSpeed = "effective_speed";
    public const string RoguelikeKeys = "rogue_like_commands";
    /// <summary>AVABand's own: spell menus ask for the book first, as Angband's do (so its keymaps work unchanged).</summary>
    public const string BookFirst = "book_first_spell_menus";
    /// <summary>AVABand's own: describe the square under the mouse at the foot of the map.</summary>
    public const string HoverLook = "hover_look";
    /// <summary>AVABand's own: a palette red–green colour blindness can tell apart.</summary>
    public const string ColorBlind = "color_blind_friendly";
    public const string ColorBlindTritan = "color_blind_tritan";
    /// <summary>AVABand's own: save by itself every few minutes of play, not only on level changes.</summary>
    public const string Autosave = "autosave_every_few_minutes";
    /// <summary>AVABand's own: a short tip the first time something happens.</summary>
    public const string Hints = "hints_for_new_players";
    /// <summary>AVABand's own: announce messages, prompts and tips to a screen reader.</summary>
    public const string ScreenReader = "screen_reader_support";
    /// <summary>AVABand's own: a moment's scene at moments of note (stairs, recall, uniques, danger, death, level types).</summary>
    public const string Scenes = "stair_scenes";
    /// <summary>AVABand's own: scenes use the bundled artwork (off: only the player's own pictures, else painted).</summary>
    public const string ScenePictures = "scene_pictures";
    /// <summary>AVABand's own: a scene stays until Space (or Enter, a click, the controller's A) instead of passing by itself.</summary>
    public const string ScenesWait = "scenes_wait_for_space";
    /// <summary>AVABand's own: torchlight fading with distance (and flickering), remembered squares dimmer.</summary>
    public const string LightAndShadow = "light_and_shadow";
    /// <summary>AVABand's own: sconces on the walls of lit rooms (so a lit room's walls tell from an unlit one's).</summary>
    public const string Sconces = "sconces_in_lit_rooms";
    public const string Shopfronts = "shopfronts_in_town";
    public const string ShopNames = "shop_names_in_town";
    /// <summary>AVABand's own: record every game as a replay (replays/ beside the saves).</summary>
    public const string RecordReplays = "record_replays";
    /// <summary>AVABand's own: the message area shows the two messages before the newest too.</summary>
    public const string ThreeMessageLines = "three_message_lines";
    /// <summary>AVABand's own: two arrow keys held together move diagonally (for keyboards without a keypad).</summary>
    public const string ArrowDiagonals = "arrow_key_diagonals";
    /// <summary>AVABand's own: a run is drawn a step at a time (off: you're shown only where it ends).</summary>
    public const string RunStepByStep = "draw_runs_step_by_step";
    /// <summary>AVABand's own: ask GitHub for a newer release when the game starts (off: only Help → Check for updates goes online).</summary>
    public const string CheckUpdatesAtStart = "check_updates_at_start";

    /// <summary>
    /// Defaults follow Angband, except that AVABand has always lit torchlight in yellow and kept the
    /// player centred, so view_yellow_light and center_player start on.
    /// </summary>
    public static readonly IReadOnlyList<OptionDef> All =
    [
        new(RoguelikeKeys, "Use the roguelike command keyset", OptionKind.Interface, false),
        new(ShowTarget, "Highlight target with cursor", OptionKind.Interface, true),
        new(HighlightPlayer, "Highlight player with cursor between turns", OptionKind.Interface, false),
        new(SolidWalls, "Show walls as solid blocks", OptionKind.Interface, false),
        new(HybridWalls, "Show walls with shaded background", OptionKind.Interface, false),
        new(YellowLight, "Color: Illuminate torchlight in yellow", OptionKind.Interface, true),
        new(CenterPlayer, "Center map continuously", OptionKind.Interface, true),
        new(PurpleUniques, "Color: Show unique monsters in purple", OptionKind.Interface, false),
        new(HpChangesColor, "Color: Player color indicates % hit points", OptionKind.Interface, true),
        new(MouseMovement, "Allow mouse clicks to move the player", OptionKind.Interface, true),
        new(EffectiveSpeed, "Show effective speed as multiplier", OptionKind.Interface, false),
        new(BookFirst, "Choose the book, then the spell (as Angband's menus)", OptionKind.Interface, false),
        new(HoverLook, "Describe the square under the mouse", OptionKind.Interface, true),
        new(ColorBlind, "Colour-blind friendly colours (red-green)", OptionKind.Interface, false),
        new(ColorBlindTritan, "Colour-blind friendly colours (blue-yellow)", OptionKind.Interface, false),
        new(Autosave, "Save every five minutes while playing", OptionKind.Interface, true),
        new(Hints, "Hints for new players (each shown once)", OptionKind.Interface, true),
        new(ScreenReader, "Screen reader support (announce messages, prompts and tips)", OptionKind.Interface, false),
        new(Scenes, "Show scenes at moments of note (stairs, recall, uniques, danger, death)", OptionKind.Interface, true),
        new(ScenePictures, "Scenes use the bundled pictures (off: painted scenes)", OptionKind.Interface, true),
        new(ScenesWait, "Scenes stay until you press Space (off: they pass by themselves)", OptionKind.Interface, false),
        new(LightAndShadow, "Light and shadow on the map (torchlight fades and flickers)", OptionKind.Interface, true),
        new(Sconces, "Sconces on the walls of lit rooms", OptionKind.Interface, true),
        new(Shopfronts, "Shopfronts in town (coloured, with windows, awnings and lanterns)", OptionKind.Interface, true),
        new(ShopNames, "Shop names over their doors in town", OptionKind.Interface, true),
        new(RecordReplays, "Record every game as a replay", OptionKind.Interface, true),
        new(ThreeMessageLines, "Show three lines of messages (off: just the newest)", OptionKind.Interface, true),
        new(ArrowDiagonals, "Two arrow keys held together move diagonally", OptionKind.Interface, true),
        new(RunStepByStep, "Draw runs a step at a time (off: show only where they end)", OptionKind.Interface, true),
        new(CheckUpdatesAtStart, "Check GitHub for a newer AVABand when the game starts", OptionKind.Interface, false),
    ];
}

/// <summary>One item type's quality ignoring (Angband's "Quality ignoring" menu).</summary>
public sealed partial class IgnoreQualityRow : ObservableObject
{
    private readonly Action<string, IgnoreLevel> _changed;

    public IgnoreQualityRow(IgnoreType type, IgnoreLevel level, Action<string, IgnoreLevel> changed)
    {
        Type = type;
        _selectedIndex = (int)level;
        _changed = changed;
    }

    public IgnoreType Type { get; }
    public string Name => Type.Name;

    /// <summary>The choices, in <see cref="IgnoreLevel"/> order: no ignore, bad, average, good, non-artifact.</summary>
    public static IReadOnlyList<string> Levels { get; } = Ignoring.LevelNames.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    public IReadOnlyList<string> Choices => Levels;

    [ObservableProperty] private int _selectedIndex;

    partial void OnSelectedIndexChanged(int value) => _changed(Type.Id, (IgnoreLevel)Math.Clamp(value, 0, (int)IgnoreLevel.All));
}

/// <summary>One option in the options list: a check box that applies as soon as it changes.</summary>
public sealed partial class OptionRow : ObservableObject
{
    private readonly Action<string, bool>? _changed;

    public OptionRow(OptionDef def, bool value, bool enabled, Action<string, bool>? changed)
    {
        Def = def;
        _isChecked = value;
        IsEnabled = enabled;
        _changed = changed;
    }

    public OptionDef Def { get; }
    public string Id => Def.Id;
    public string Description => Def.Description;
    public bool IsEnabled { get; }

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed?.Invoke(Id, value);
}

// Angband's options menu ('='): interface options apply at once and are kept in the settings; birth
// options are shown as the character was created; cheat options can be switched on, at a price.
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<OptionRow> InterfaceOptionRows { get; } = [];
    public ObservableCollection<OptionRow> BirthOptionRows { get; } = [];
    public ObservableCollection<OptionRow> CheatOptionRows { get; } = [];
    public ObservableCollection<IgnoreQualityRow> IgnoreQualityRows { get; } = [];

    /// <summary>Raised by '=' to open the settings on the options page.</summary>
    public event Action? OptionsRequested;

    [RelayCommand]
    public void ShowOptions() => OptionsRequested?.Invoke();

    [ObservableProperty] private bool _centerPlayer = true;

    public string CheaterText => _game.IsCheater
        ? (_game.UsedDebug ? "This character has used debug commands and will not enter the score table."
            : "This character has cheated and will not enter the score table.")
        : "Switching a cheat on keeps this character out of the score table for good.";

    private static OptionDef? InterfaceOption(string id) =>
        DisplayOptions.All.FirstOrDefault(o => o.Id == id) ?? OptionCatalog.Find(id);

    /// <summary>An interface option's value: the player's setting, or its default.</summary>
    public bool OptionValue(string id) =>
        _settings.Options.TryGetValue(id, out var v) ? v : InterfaceOption(id)?.Default ?? false;

    /// <summary>Changes an interface option (saved in the settings) or a cheat option (on the character).</summary>
    public void SetOption(string id, bool value)
    {
        var def = InterfaceOption(id);
        if (def is null) return;
        if (def.Kind == OptionKind.Cheat)
        {
            _game.SetOption(id, value);
            OnPropertyChanged(nameof(CheaterText));
        }
        else if (def.Kind == OptionKind.Interface)
        {
            _settings.Options[id] = value;
            SaveSettings();
            _game.SetOption(id, value); // ignored for display-only options
            ApplyDisplayOptions();
            UpdateMessageLines();
            // Angband rogue_like_commands: switching it puts in the whole keyset.
            if (id == DisplayOptions.RoguelikeKeys)
                UseKeyset(value ? InputBindings.Keyset.Roguelike : InputBindings.Keyset.Original);
        }
        RefreshInventory();
        Refresh();
    }

    /// <summary>Hands the settings' options to the game and the map (on start, load and change).</summary>
    private void ApplyOptions()
    {
        _game.ApplyInterfaceOptions(OptionCatalog.OfKind(OptionKind.Interface).ToDictionary(o => o.Id, o => OptionValue(o.Id)));
        ApplyDisplayOptions();
        UpdateMessageLines();
        RefreshOptionRows();
    }

    private void ApplyDisplayOptions()
    {
        _cells.SolidWalls = OptionValue(DisplayOptions.SolidWalls);
        _cells.HybridWalls = OptionValue(DisplayOptions.HybridWalls);
        _cells.YellowLight = OptionValue(DisplayOptions.YellowLight);
        _cells.PurpleUniques = OptionValue(DisplayOptions.PurpleUniques);
        _cells.HpChangesColor = OptionValue(DisplayOptions.HpChangesColor);
        _cells.ColorBlind = OptionValue(DisplayOptions.ColorBlind);
        _cells.ColorBlindTritan = OptionValue(DisplayOptions.ColorBlindTritan);
        CenterPlayer = OptionValue(DisplayOptions.CenterPlayer);
    }

    /// <summary>Rebuilds the option lists (after a new or loaded character).</summary>
    public void RefreshOptionRows()
    {
        InterfaceOptionRows.Clear();
        foreach (var o in OptionCatalog.OfKind(OptionKind.Interface).Concat(DisplayOptions.All))
            InterfaceOptionRows.Add(new OptionRow(o, OptionValue(o.Id), true, SetOption));
        BirthOptionRows.Clear();
        foreach (var o in OptionCatalog.OfKind(OptionKind.Birth))
            BirthOptionRows.Add(new OptionRow(o, _game.Options[o.Id], false, null));
        CheatOptionRows.Clear();
        foreach (var o in OptionCatalog.OfKind(OptionKind.Cheat))
            CheatOptionRows.Add(new OptionRow(o, _game.Options[o.Id], true, SetOption));
        OnPropertyChanged(nameof(CheaterText));
        IgnoreQualityRows.Clear();
        foreach (var type in Ignoring.Types)
            IgnoreQualityRows.Add(new IgnoreQualityRow(type, _game.Ignore.QualityFor(type.Id), (id, level) =>
            {
                _game.SetIgnoreQuality(id, level);
                RefreshInventory();
                Refresh();
            }));
    }

    /// <summary>The player outlined between turns (highlight_player), unless looking around.</summary>
    public Loc? Highlight => OptionValue(DisplayOptions.HighlightPlayer) && !IsLooking ? _game.Player.Position : null;

    /// <summary>Angband's status-bar speed: "Fast (+10)", or with effective_speed "Fast (x2.0)".</summary>
    private string SpeedText()
    {
        var speed = _game.Player.Speed;
        if (speed == 0) return "";
        var label = speed > 0 ? "Fast" : "Slow";
        if (!OptionValue(DisplayOptions.EffectiveSpeed)) return $"{label} ({speed:+0;-0})";
        var multiplier = EnergyTable.EnergyPerTurn(speed) / (double)EnergyTable.EnergyPerTurn(0);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{label} (x{multiplier:0.0})");
    }
}
