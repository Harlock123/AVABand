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

    private bool _identifyingSymbol;

    /// <summary>Waiting for a symbol: the monster letter to banish, or the symbol to identify ('/').</summary>
    public bool IsChoosingGlyph => _banishItem is not null || _identifyingSymbol;

    /// <summary>'/': asks for a symbol to identify (Angband do_cmd_query_symbol).</summary>
    public void BeginIdentifySymbol()
    {
        _identifyingSymbol = true;
        LastMessage = "Enter character to be identified:";
    }

    /// <summary>
    /// Says what a symbol stands for; if you have met monsters shown with it, offers their recall
    /// ("Recall details?"), which opens the monster knowledge narrowed to them.
    /// </summary>
    private void IdentifySymbol(char symbol)
    {
        var what = _game.IdentifySymbol(symbol);
        var known = _game.KnownMonstersWithSymbol(symbol);
        if (known.Count == 0)
        {
            AddMessage(what);
            return;
        }
        AskYesNo($"{what}  Recall details?", yes =>
        {
            if (yes) KnowledgeRequested?.Invoke(CreateKnowledge(CreateMonsterKnowledge(symbol)));
            else LastMessage = what;
        });
    }

    private void BeginGlyphChoice(Item item, bool activate)
    {
        _banishItem = item;
        _banishByActivation = activate;
        LastMessage = "Choose a monster race (by symbol) to banish:";
    }

    /// <summary>
    /// Remove Curse (Angband effect_handler_REMOVE_CURSE): "Uncurse which item?", then "Remove which
    /// curse (spell strength 20+d20)?" with each curse's strength, then the command with the choice.
    /// With nothing to uncurse, the command goes ahead and says so.
    /// </summary>
    private void BeginUncurse(Func<CurseChoice?, GameCommand> command, string strength)
    {
        var items = _game.UncursableItems();
        if (items.Count == 0)
        {
            Execute(command(null));
            return;
        }
        ShowMenu("Uncurse which item?", [.. items.Select(item => (_game.Describe(item), (Action)(() =>
            ShowMenu($"Remove which curse (spell strength {strength})?",
            [
                .. _game.RemovableCurses(item).Select(c => (
                    $"{_data.Curse(c.Curse)?.Name ?? c.Curse} (curse strength {c.Power})",
                    (Action)(() => Execute(command(new CurseChoice(item, c.Curse)))))),
            ]))))]);
    }

    /// <summary>The key typed after a banishment prompt: a monster letter, or Escape to cancel.</summary>
    public bool ChooseGlyph(string symbol)
    {
        if (_identifyingSymbol)
        {
            _identifyingSymbol = false;
            if (symbol.Length != 1 || char.IsWhiteSpace(symbol[0])) LastMessage = "Cancelled.";
            else IdentifySymbol(symbol[0]);
            return true;
        }
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
