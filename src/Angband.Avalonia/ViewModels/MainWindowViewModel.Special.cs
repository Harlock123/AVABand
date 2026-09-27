using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Input;

namespace Angband.Avalonia.ViewModels;

// Prompts for Angband's special effects: which monster letter to banish, and changing back from
// a shape before using items or spells.
public sealed partial class MainWindowViewModel
{
    private Item? _banishItem;
    private bool _banishByActivation;
    private bool _confirmResumeShape;

    /// <summary>Waiting for the monster letter to banish.</summary>
    public bool IsChoosingGlyph => _banishItem is not null;

    private void BeginGlyphChoice(Item item, bool activate)
    {
        _banishItem = item;
        _banishByActivation = activate;
        LastMessage = "Choose a monster race (by symbol) to banish:";
    }

    /// <summary>The key typed after a banishment prompt: a monster letter, or Escape to cancel.</summary>
    public bool ChooseGlyph(string symbol)
    {
        if (_banishItem is not { } item) return false;
        _banishItem = null;
        if (symbol.Length != 1 || char.IsWhiteSpace(symbol[0]))
        {
            LastMessage = "Cancelled.";
            return true;
        }
        Execute(_banishByActivation ? new ActivateCommand(item, Glyph: symbol[0]) : new UseCommand(item, Glyph: symbol[0]));
        return true;
    }

    /// <summary>Actions a changed shape can't take without changing back first (Angband asks).</summary>
    private static readonly HashSet<InputAction> NeedHands =
    [
        InputAction.Wield, InputAction.TakeOff, InputAction.Quaff, InputAction.Read, InputAction.Eat, InputAction.Throw,
        InputAction.Refuel, InputAction.Fire, InputAction.Cast, InputAction.Study, InputAction.AimWand,
        InputAction.UseStaff, InputAction.ZapRod, InputAction.Activate,
    ];

    /// <summary>In a shape, an item or spell command offers to change back instead. True if it did.</summary>
    private bool OfferToResumeShape(InputAction action)
    {
        if (_game.PlayerShape is not { } shape || !NeedHands.Contains(action)) return false;
        _confirmResumeShape = true;
        IsConfirming = true;
        LastMessage = $"You cannot do this while in {shape.Name} form. Change back to your normal shape? (y/n)";
        return true;
    }

    /// <summary>Answers the change-back question (from Confirm). True if it was that question.</summary>
    private bool ConfirmResumeShape(bool yes)
    {
        if (!_confirmResumeShape) return false;
        _confirmResumeShape = false;
        if (yes) Execute(new ResumeShapeCommand());
        else LastMessage = "Cancelled.";
        return true;
    }
}
