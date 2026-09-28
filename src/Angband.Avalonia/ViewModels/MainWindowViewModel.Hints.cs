using Angband.Avalonia.Input;
using Angband.Core.Game;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Hints for new players (AVABand's own): the first time something happens — a shop, a monster, a
// trap, a bad wound, hunger, a failing light, an item with unknown runes — a short tip appears at
// the foot of the map, naming the keys you actually have. Each is shown once (remembered in the
// settings, so not again for the next character) and goes after a few commands or with ×. The
// option "Hints for new players" turns them off.
public sealed partial class MainWindowViewModel
{
    /// <summary>How many commands a hint stays up for.</summary>
    public const int HintCommands = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hintText = "";

    public bool HasHint => HintText.Length > 0;

    private int _hintLeft;

    [RelayCommand]
    public void DismissHint()
    {
        HintText = "";
        _hintLeft = 0;
        if (_game.IsTutorial) _tutorialDismissed = true; // until the next step
    }

    /// <summary>The key (or keys) bound to an action, as the player would press it.</summary>
    private string KeyName(InputAction action)
    {
        var chord = Bindings?.KeysFor(action).OrderBy(c => c.StartsWith("Char:", StringComparison.Ordinal) ? 0 : 1).FirstOrDefault();
        return chord is null ? "(unbound)" : chord.StartsWith("Char:", StringComparison.Ordinal) ? chord[5..] : KeyboardInput.Display(chord);
    }

    /// <summary>After each command: the hint showing has one command fewer to go.</summary>
    private void CountDownHint()
    {
        if (_game.IsTutorial) return; // the tutorial's steps stay up until done
        if (HasHint && --_hintLeft <= 0) DismissHint();
    }

    /// <summary>After each refresh: shows the next hint due, if none is up.</summary>
    private void CheckHints()
    {
        if (CheckTutorial()) return;
        if (_game.IsReplay) return;
        if (HasHint || !OptionValue(DisplayOptions.Hints) || _game.Player.IsDead) return;
        foreach (var (id, due, text) in Hints())
        {
            if (_settings.SeenHints.Contains(id) || !due()) continue;
            _settings.SeenHints.Add(id);
            _saveSettings?.Invoke(_settings);
            HintText = text();
            _hintLeft = HintCommands;
            return;
        }
    }

    /// <summary>The squares within <paramref name="radius"/> of the player.</summary>
    private IEnumerable<Angband.Core.Geometry.Loc> Near(int radius)
    {
        var at = _game.Player.Position;
        for (var y = at.Y - radius; y <= at.Y + radius; y++)
        for (var x = at.X - radius; x <= at.X + radius; x++)
            if (_game.Level.InBounds(new(x, y)) && new Angband.Core.Geometry.Loc(x, y).DistanceTo(at) <= radius) yield return new(x, y);
    }

    /// <summary>The hints, in order of importance: an id, when it is due, and what it says.</summary>
    private IEnumerable<(string Id, Func<bool> Due, Func<string> Text)> Hints()
    {
        var game = _game;
        var player = game.Player;
        yield return ("tutorial", () => player.Depth == 0 && !game.IsTutorial,
            () => "New to Angband? Game → Tutorial teaches the basics on a short level of its own, in a few minutes.");
        yield return ("hurt", () => player.Hp * 10 < player.MaxHp * 3 && player.MaxHp > 0,
            () => $"You are badly hurt! Quaff a potion of Cure Light Wounds ({KeyName(InputAction.Quaff)}), read Phase Door ({KeyName(InputAction.Read)}) to get away, or take the stairs.");
        yield return ("shop", () => IsInStore,
            () => $"In a shop, a letter buys (and asks how many; Shift+letter takes the whole pile), Tab switches to selling, Esc leaves; {KeyName(InputAction.Inspect)} examines the highlighted item.");
        yield return ("monster", () => player.Depth > 0 && game.Level.Monsters.All.Any(m => m.IsVisible),
            () => $"A monster! Walk into it to attack. {KeyName(InputAction.Look)} looks at it (the sidebar then recalls what you know); right-click it for everything you can do.");
        yield return ("trap", () => Near(10).Any(p => game.Known.IsKnown(p) && game.VisibleTrapAt(p) is not null),
            () => $"A ^ is a trap. Walking at it tries to disarm it ({KeyName(InputAction.Disarm)} does too); {KeyName(InputAction.WalkIntoTrap)} walks onto it on purpose.");
        yield return ("floor", () => game.Level.Objects.At(player.Position).Any(i => !i.IsGold && !Hidden(i)),
            () => $"{KeyName(InputAction.Pickup)} picks up what you stand on (it is listed under \"On the floor\" in the sidebar).");
        yield return ("hungry", () => game.HungerLevel <= HungerLevel.Hungry,
            () => $"You are hungry: eat something ({KeyName(InputAction.Eat)}). Rations are sold in the General Store (1).");
        yield return ("light", () => player.Inventory.Light is { Fuel: > 0 and < 1000 } light && light.Kind.Fuel > 0,
            () => $"Your light is running low. Refuel it ({KeyName(InputAction.Refuel)}) with a Flask of Oil or another torch, or wield a new one ({KeyName(InputAction.Wield)}).");
        yield return ("runes", () => player.Inventory.Equipped.Any(i => !game.Knowledge.IsFullyKnown(i) && i.Runes().Any()),
            () => $"Something you wear has runes you don't know yet. They show themselves as you use it or are hit; {KeyName(InputAction.Inspect)} shows what you know so far.");
        yield return ("level", () => player.Level >= 2,
            () => $"You went up a level: more hit points and better skills. The character sheet ({KeyName(InputAction.CharacterSheet)}) shows them.");
        yield return ("stairs", () => player.Depth >= 1,
            () => "You are in the dungeon: > goes down, < up. Deeper is richer and deadlier; a Word of Recall scroll takes you to town and back to your deepest level.");
    }
}
