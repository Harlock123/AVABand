using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Input;

namespace Angband.Avalonia.ViewModels;

// The map's right-click menus (Angband ui-context.c): on yourself, context_menu_player (use, cast,
// the stairs, look, rest, pick up, character, centre the map, and "Other" for the lists and
// screens); on any other square, context_menu_cave (look at it, recall the monster there, use an
// item, cast, fire or throw at it; next to you, attack, steal, open, close, disarm, jump onto or
// tunnel; further off, travel, walk or run towards it). A letter, a click or the D-pad picks.
public sealed partial class MainWindowViewModel
{
    private List<Action>? _menuActions;

    /// <summary>The menu's entries, for tests: the labels in order.</summary>
    public IReadOnlyList<string> MenuLabels => _menuActions is null ? [] : [.. ChoiceRows.Select(r => r.Text)];

    /// <summary>Right-click on a map square: the player's menu on yourself, the square's elsewhere.</summary>
    public void OpenContextMenu(Loc loc)
    {
        if (IsPrompting || IsConfirming || IsInStore || IsLooking || !_game.Level.InBounds(loc)) return;
        if (loc == _game.Player.Position)
        {
            ShowMenu("", PlayerMenu());
            return;
        }
        // What is there (said as well, as a right-click always has): the monster, or the square.
        var what = _game.Level.Monsters.At(loc) is { IsVisible: true } m && !_game.IsHallucinating
            ? Capitalize(_game.LookDescription(m)) + "."
            : DescribeSquare(loc);
        ShowMenu(what.TrimEnd('.') + ":", SquareMenu(loc));
        LastMessage = what;
    }

    /// <summary>From a gamepad (or a key bound to it): the menu for the square under the look cursor, or for you.</summary>
    public void OpenContextMenuHere()
    {
        if (IsLooking && Cursor is { } at)
        {
            StopLooking();
            OpenContextMenu(at);
        }
        else OpenContextMenu(_game.Player.Position);
    }

    /// <summary>A paragraph under the menu's title (a quest's words), or empty.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _promptText = "";

    private void ShowMenu(string title, List<(string Label, Action Act)> entries, string text = "")
    {
        PromptRows.Clear();
        SpellPromptRows.Clear();
        ChoiceRows.Clear();
        _menuActions = [.. entries.Select(e => e.Act)];
        for (var i = 0; i < entries.Count; i++) ChoiceRows.Add(new ChoiceRow(((char)('a' + i)).ToString(), entries[i].Label));
        PromptTitle = title.Length > 0 ? title : "Choose:";
        PromptText = text;
        PromptSelectedIndex = 0;
        IsPrompting = true;
    }

    /// <summary>A letter in a context menu; anything else cancels.</summary>
    private void ChooseMenu(char key)
    {
        var actions = _menuActions!;
        _menuActions = null;
        ChoiceRows.Clear();
        var index = key - 'a';
        if (index < 0 || index >= actions.Count)
        {
            LastMessage = "Cancelled.";
            return;
        }
        actions[index]();
    }

    private bool CanCastSpells => _game.Player.Class?.Realm is not null;

    /// <summary>Angband context_menu_player.</summary>
    private List<(string, Action)> PlayerMenu()
    {
        var level = _game.Level;
        var here = _game.Player.Position;
        var menu = new List<(string, Action)> { ("Use", () => HandleAction(InputAction.UseItem)) };
        if (CanCastSpells) menu.Add(("Cast", () => HandleAction(InputAction.Cast)));
        if (level.Has(here, TerrainFlags.UpStair)) menu.Add(("Go up", () => HandleAction(InputAction.StairsUp)));
        if (level.Has(here, TerrainFlags.DownStair)) menu.Add(("Go down", () => HandleAction(InputAction.StairsDown)));
        menu.Add(("Look", Look));
        menu.Add(("Rest", () => HandleAction(InputAction.Rest)));
        if (level.Objects.At(here).Any(i => !Hidden(i))) menu.Add(("Pick up", () => HandleAction(InputAction.Pickup)));
        menu.Add(("Character", ShowCharacterSheet));
        if (!CenterPlayer) menu.Add(("Center map", CenterMap));
        menu.Add(("Other", () => ShowMenu("", OtherMenu())));
        return menu;
    }

    /// <summary>Angband context_menu_player_2: the lists and screens.</summary>
    private List<(string, Action)> OtherMenu() =>
    [
        ("Knowledge", ShowKnowledge),
        ("Your journey", ShowJourney),
        ("Show map", ShowOverviewMap),
        ("Show messages", ShowMessageHistory),
        ("Show monster list", ShowMonsterList),
        ("Show object list", ShowObjectList),
        ("Toggle ignored", () => HandleAction(InputAction.ToggleIgnore)),
        ("Ignore an item", () => HandleAction(InputAction.Ignore)),
        ("Clear out junk", BeginClearJunk),
        ("Tidy pack", TidyPack),
        ("Options", ShowOptions),
        ("Commands", ShowKeyCommands),
    ];

    /// <summary>Angband context_menu_cave.</summary>
    private List<(string, Action)> SquareMenu(Loc loc)
    {
        var level = _game.Level;
        var player = _game.Player.Position;
        var known = _game.Known.IsKnown(loc);
        var monster = level.Monsters.At(loc) is { IsVisible: true } m ? m : null;
        var dir = DirectionExtensions.FromOffset(loc.X - player.X, loc.Y - player.Y);
        var adjacent = Math.Max(Math.Abs(loc.X - player.X), Math.Abs(loc.Y - player.Y)) == 1;

        var menu = new List<(string, Action)> { ("Look at", () => LookAt(loc)) };
        if (monster is not null && !_game.IsHallucinating) menu.Add(("Recall info", () => ShowRecall(monster.Race)));
        menu.Add(("Use item on", () => AimAt(loc, InputAction.UseItem)));
        if (CanCastSpells) menu.Add(("Cast on", () => AimAt(loc, InputAction.Cast)));
        if (adjacent)
        {
            if (monster is not null) menu.Add(("Attack", () => Execute(new WalkCommand(dir))));
            if (known && _game.ChestAt(loc) is not null) menu.Add(("Open chest", () => Execute(new OpenCommand(dir))));
            if (monster is not null && _game.ClassHas(ClassFlags.Steal)) menu.Add(("Steal", () => Execute(new StealCommand(dir))));
            if (_game.VisibleTrapAt(loc) is not null)
            {
                menu.Add(("Disarm", () => Execute(new DisarmCommand(dir))));
                menu.Add(("Jump onto", () => Execute(new JumpCommand(dir))));
            }
            if (known && level.IsDoor(loc) && level.IsPassable(loc)) menu.Add(("Close", () => Execute(new CloseCommand(dir))));
            else if (known && level.Has(loc, TerrainFlags.DoorClosed)) menu.Add(("Open", () => Execute(new OpenCommand(dir))));
            else if (known && !level.IsPassable(loc) && !level.IsPermanent(loc)
                     && level.FeatureAt(loc).HasAny(TerrainFlags.Rock | TerrainFlags.Rubble))
                menu.Add(("Tunnel", () => Execute(new TunnelCommand(dir))));
            menu.Add(("Walk towards", () => Execute(new WalkCommand(dir))));
        }
        else
        {
            menu.Add(("Pathfind to", () => Execute(new TravelCommand(loc))));
            menu.Add(("Walk towards", () => Execute(new WalkCommand(dir))));
            menu.Add(("Run towards", () => StartRun(dir)));
        }
        if (_game.Player.Inventory.Bow is not null) menu.Add(("Fire on", () => AimAt(loc, InputAction.Fire)));
        menu.Add(("Throw to", () => AimAt(loc, InputAction.Throw)));
        return menu;
    }

    /// <summary>Look mode, starting on this square (Angband target_set_interactive from a point).</summary>
    private void LookAt(Loc loc)
    {
        EnterCursor(CursorMode.Look);
        _cursor = loc;
        _cursorFree = true;
        ShowCursor();
    }

    /// <summary>
    /// Targets the square (or the monster on it), then starts the command, which aims there: the
    /// menu's "Fire on", "Cast on" and so on (Angband sets DIR_TARGET on the command).
    /// </summary>
    private void AimAt(Loc loc, InputAction action)
    {
        if (_game.Level.Monsters.At(loc) is { IsVisible: true } m) _game.SetTarget(m);
        else _game.SetTarget(loc);
        _game.AimAtTargetNext = true;
        HandleAction(action);
        if (!IsPrompting) _game.AimAtTargetNext = false; // done (or never started)
    }
}
