using Angband.Avalonia.Input;
using Angband.Core.Game;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Hints for new players (AVABand's own): the first time something happens — a shop, a monster, a
// trap, a bad wound, hunger, a failing light, an item with unknown runes, and each of the conditions
// that kill new characters (blindness, confusion, poison, paralysis, fear, cuts, stunning, slowness,
// darkness, drained stats) with what cures it — a short tip appears at
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

    /// <summary>Whether this game's character has been paralysed (it's over before a hint could see it).</summary>
    private bool _sawParalysis;

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
        // Conditions, with what cures them (as this game's potions do).
        var timed = player.Timed;
        var quaff = KeyName(InputAction.Quaff);
        yield return ("paralysed", () => _sawParalysis,
            () => "You were paralysed — helpless while everything near you acted. Gear of Free Action stops it; until you have "
                  + "some, never trade blows with things that paralyse (a floating eye's gaze, a ghoul's touch, carrion crawlers' stings).");
        yield return ("blind", () => timed.Has(Angband.Core.Effects.TimedIds.Blind),
            () => $"You are blind: you can't see the map or read scrolls. A potion of Cure Light Wounds cures it ({quaff}); potions and staffs still work.");
        yield return ("confused", () => timed.Has(Angband.Core.Effects.TimedIds.Confused),
            () => $"You are confused: you stagger about and can't read scrolls. Cure Serious Wounds cures it ({quaff}; Cure Light Wounds eases it).");
        yield return ("poisoned", () => timed.Has(Angband.Core.Effects.TimedIds.Poisoned),
            () => $"You are poisoned, and lose hit points every turn until it wears off. Neutralize Poison or Cure Critical Wounds cures it ({quaff}).");
        yield return ("afraid", () => timed.Has(Angband.Core.Effects.TimedIds.Afraid),
            () => $"You are afraid: you can't fight hand to hand, but you can still shoot, cast and use things. Potions of Boldness, Heroism or Berserk Strength cure fear ({quaff}).");
        yield return ("stun", () => timed.Has(Angband.Core.Effects.TimedIds.Stun),
            () => $"You are stunned: you fight and cast worse, and a heavy stun knocks you out. Get away from what hit you; Cure Critical Wounds cures it ({quaff}).");
        yield return ("cut", () => timed.Has(Angband.Core.Effects.TimedIds.Cut),
            () => $"You are bleeding. Small cuts heal by themselves; Cure Light Wounds eases a cut and Cure Serious Wounds stops it ({quaff}).");
        yield return ("slow", () => timed.Has(Angband.Core.Effects.TimedIds.Slow),
            () => "You are slowed: everything around you gets more turns than you. Get clear until it wears off — a potion of Speed evens it.");
        yield return ("image", () => timed.Has(Angband.Core.Effects.TimedIds.Image),
            () => "You are hallucinating: the monsters and things you see aren't what's there. It wears off; until then, be careful what you attack.");
        yield return ("amnesia", () => timed.Has(Angband.Core.Effects.TimedIds.Amnesia),
            () => $"You are amnesiac: your spells and what you know of things may fail you. It wears off; Cure Critical Wounds cures it ({quaff}).");
        yield return ("dark", () => player.Depth > 0 && (player.Inventory.Light is not { } lamp || (lamp.Kind.Fuel > 0 && lamp.Fuel <= 0)),
            () => $"You have no light: you can't read, and you see only lit rooms. Wield a torch or lantern ({KeyName(InputAction.Wield)}), or refuel yours ({KeyName(InputAction.Refuel)}).");
        yield return ("drained", () => player.StatDrain.Values.Any(v => v > 0),
            () => "One of your stats was drained. Gaining a level restores drained stats; gear that sustains a stat protects it.");
        yield return ("exp_drained", () => player.Experience < player.MaxExperience,
            () => "Your experience was drained. A potion of Restore Life Levels brings it back; gear with Hold Life protects it.");
        yield return ("shop", () => IsInStore,
            () => $"In a shop, a letter buys (and asks how many; Shift+letter takes the whole pile), Tab switches to selling, Esc leaves; {KeyName(InputAction.Inspect)} examines the highlighted item.");
        yield return ("monster", () => player.Depth > 0 && game.Level.Monsters.All.Any(m => m.IsVisible),
            () => $"A monster! Walk into it to attack. {KeyName(InputAction.Look)} looks at it (the sidebar then recalls what you know); right-click it for everything you can do.");
        yield return ("trap", () => Near(10).Any(p => game.Known.IsKnown(p) && game.VisibleTrapAt(p) is not null),
            () => $"A ^ is a trap. Walking at it tries to disarm it ({KeyName(InputAction.Disarm)} does too); {KeyName(InputAction.WalkIntoTrap)} walks onto it on purpose.");
        yield return ("hidden_traps", () => game.TrapsFound > 0,
            () => "You noticed that trap as it came into sight. Traps stay hidden until your search skill is up to "
                  + $"them; it grows with your level and with gear of Searching (your character sheet, {KeyName(InputAction.CharacterSheet)}, shows it).");
        yield return ("pack_full", () => player.Inventory.SlotsUsed >= player.Inventory.PackSize
                                         && game.Level.Objects.At(player.Position).Any(i => !i.IsGold && !Hidden(i) && !player.Inventory.CanCarry(i)),
            () => "Your pack is full, so you can't pick this up. Drop something, eat or drink it, or use Game → Clear out junk; a bag of holding gives more room.");
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
