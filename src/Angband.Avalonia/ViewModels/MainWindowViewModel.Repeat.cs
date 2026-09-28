using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Repeat the last command (Angband 'n', roguelike Ctrl+V): cast the same spell, fire or throw again,
/// use the same kind of item, keep digging or disarming. Aimed commands aim afresh — at the current
/// target, else the nearest monster — as they did the first time.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private GameCommand? _lastCommand;

    /// <summary>The command Ctrl+V would repeat, if any.</summary>
    public GameCommand? LastCommand => _lastCommand;

    /// <summary>Commands worth repeating; moving, stairs, shopping and equipment changes are not.</summary>
    private static bool IsRepeatable(GameCommand command) => command is CastCommand or FireCommand or ThrowCommand
        or UseCommand or ActivateCommand or TunnelCommand or DisarmCommand or OpenCommand or CloseCommand
        or StealCommand or RefuelCommand or RestCommand or HoldCommand;

    /// <summary>Remembers a repeatable command that was carried out (took game time).</summary>
    private void NoteForRepeat(GameCommand command, bool tookTime)
    {
        if (tookTime && IsRepeatable(command)) _lastCommand = command;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void RepeatLastCommand()
    {
        if (_lastCommand is not { } last)
        {
            AddMessage("There is no command to repeat.");
            Refresh();
            return;
        }
        if (WithCurrentItem(last) is not { } again)
        {
            AddMessage($"You have no more {_game.Describe(ItemOf(last)!, withArticle: false)}.");
            Refresh();
            return;
        }
        // Casting beyond your mana is asked about each time, never repeated silently.
        if (again is CastCommand { AllowOverexert: true } cast) again = cast with { AllowOverexert = false };
        Execute(again);
    }

    private static Item? ItemOf(GameCommand command) => command switch
    {
        ThrowCommand t => t.Item,
        UseCommand u => u.Item,
        ActivateCommand a => a.Item,
        RefuelCommand r => r.Fuel,
        FireCommand f => f.Ammo,
        _ => null,
    };

    /// <summary>
    /// The command again, with its item still to hand: the same stack if it's still carried (or
    /// underfoot), else another of the same kind; null when there is none left.
    /// </summary>
    private GameCommand? WithCurrentItem(GameCommand command)
    {
        if (ItemOf(command) is not { } item) return command;
        var carried = _game.Player.Inventory.All.Concat(_game.Level.Objects.At(_game.Player.Position)).ToList();
        if (carried.Any(i => ReferenceEquals(i, item) && i.Number > 0)) return command;
        if (carried.FirstOrDefault(i => i.Kind == item.Kind && i.Number > 0) is not { } other) return null;
        return command switch
        {
            ThrowCommand t => t with { Item = other },
            UseCommand u => u with { Item = other },
            ActivateCommand a => a with { Item = other },
            RefuelCommand r => r with { Fuel = other },
            FireCommand f => f with { Ammo = other },
            _ => command,
        };
    }
}
