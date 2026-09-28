using Angband.Core.Game;
using Angband.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Word of Recall's questions (Angband effect_handler_RECALL): below the deepest level reached,
/// "Set recall depth to current depth?"; in town, in a persistent dungeon, which kept level to go
/// back to. Asked before the scroll, rod, spell or activation is used; the answer goes with it.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private bool _recallAnswered;

    // A number typed in answer to a question (the recall level), shown on the message line.
    private (string Question, int Default, Action<int> Done, int Most)? _numberPrompt;
    private string _numberText = "";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isEnteringNumber;

    /// <summary>Asks first, when the command would start a recall that has something to decide; true if it asked.</summary>
    private bool AskAboutRecall(GameCommand command)
    {
        if (_recallAnswered)
        {
            _recallAnswered = false;
            return false;
        }
        if (!_game.StartsRecall(command)) return false;
        var p = _game.Player;
        if (p.Depth > 0 && p.Depth != p.MaxDepth)
        {
            AskYesNo("Set recall depth to current depth?", yes =>
            {
                _game.RecallSetsDepth = yes;
                _recallAnswered = true;
                Execute(command);
            });
            return true;
        }
        if (p.Depth == 0 && _game.PersistentLevels && _game.RecallChoices.Count > 0)
        {
            var deepest = _game.RecallChoices.Max();
            AskRecallLevel(command, p.RecallDepth > 0 && _game.RecallChoices.Contains(p.RecallDepth) ? p.RecallDepth : deepest);
            return true;
        }
        return false;
    }

    private void AskRecallLevel(GameCommand command, int suggested) =>
        BeginNumberPrompt("Which level do you wish to return to (0 to cancel)?", suggested, level =>
        {
            if (level == 0)
            {
                LastMessage = "Cancelled.";
                return;
            }
            if (!_game.RecallChoices.Contains(level))
            {
                AddMessage("You must choose a level you have previously visited.");
                AskRecallLevel(command, suggested);
                return;
            }
            _game.RecallChoice = level;
            _recallAnswered = true;
            Execute(command);
        });

    /// <summary>Asks for a number: digits, Backspace, Enter (the suggestion if nothing is typed), Escape.</summary>
    private void BeginNumberPrompt(string question, int suggested, Action<int> done, int most = 999)
    {
        _numberPrompt = (question, suggested, done, most);
        _numberText = "";
        IsEnteringNumber = true;
        ShowNumberPrompt();
    }

    private void ShowNumberPrompt()
    {
        if (_numberPrompt is { } prompt)
            LastMessage = $"{prompt.Question} {(_numberText.Length > 0 ? _numberText : prompt.Default.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
    }

    /// <summary>A key while a number is asked for; every key is taken.</summary>
    public void NumberKey(string? symbol, bool backspace, bool escape, bool enter)
    {
        if (_numberPrompt is not { } prompt) return;
        if (escape)
        {
            _numberPrompt = null;
            IsEnteringNumber = false;
            LastMessage = "Cancelled.";
            return;
        }
        if (enter)
        {
            _numberPrompt = null;
            IsEnteringNumber = false;
            prompt.Done(_numberText.Length > 0 ? int.Parse(_numberText, System.Globalization.CultureInfo.InvariantCulture) : prompt.Default);
            Refresh();
            return;
        }
        if (backspace && _numberText.Length > 0) _numberText = _numberText[..^1];
        else if (symbol is { Length: 1 } s && char.IsAsciiDigit(s[0]) && _numberText.Length < 3) _numberText += s;
        ShowNumberPrompt();
    }

    /// <summary>
    /// The controller (or arrow keys) with a number asked for: up and down change it by one, right
    /// and left by ten, A confirms, B cancels. Always taken.
    /// </summary>
    private void NumberAction(InputAction action)
    {
        if (_numberPrompt is not { } prompt) return;
        var current = _numberText.Length > 0 ? int.Parse(_numberText, System.Globalization.CultureInfo.InvariantCulture) : prompt.Default;
        var step = action switch
        {
            InputAction.MoveNorth => 1, InputAction.MoveSouth => -1,
            InputAction.MoveEast => 10, InputAction.MoveWest => -10,
            _ => 0,
        };
        if (step != 0)
        {
            _numberText = Math.Clamp(current + step, 1, prompt.Most).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ShowNumberPrompt();
        }
        else if (action == InputAction.Confirm) NumberKey(null, false, false, enter: true);
        else if (action == InputAction.Cancel) NumberKey(null, false, escape: true, false);
    }
}
