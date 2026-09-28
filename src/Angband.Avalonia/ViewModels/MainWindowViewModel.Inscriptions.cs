using Angband.Core.Game;
using Angband.Core.Items;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

// Inscriptions in the UI: '{' asks for an item and then the text, '}' removes one, and commands on
// items inscribed with !<command> or !* ask first (Angband get_item_allow).
public sealed partial class MainWindowViewModel
{
    private Item? _inscribing;
    private Action? _confirmAction;

    /// <summary>The inscription being written (a text box over the map while true).</summary>
    [ObservableProperty] private bool _isInscribing;
    [ObservableProperty] private string _inscriptionText = "";
    [ObservableProperty] private string _inscriptionTitle = "";

    private void BeginInscription(Item item)
    {
        _inscribing = item;
        InscriptionText = item.Note ?? "";
        InscriptionTitle = $"Inscribe {_game.Describe(item)} with:";
        IsInscribing = true;
    }

    /// <summary>Enter: writes the inscription (an empty one removes it).</summary>
    public void CommitInscription()
    {
        if (!IsInscribing || _inscribing is not { } item) return;
        IsInscribing = false;
        _inscribing = null;
        Execute(new InscribeCommand(item, InscriptionText));
    }

    /// <summary>Escape: leaves the item as it was.</summary>
    public void CancelInscription()
    {
        if (!IsInscribing) return;
        IsInscribing = false;
        _inscribing = null;
        LastMessage = "Cancelled.";
    }

    /// <summary>Asks a yes/no question, running <paramref name="action"/> on yes.</summary>
    private void AskFirst(string question, Action action)
    {
        _confirmAction = action;
        IsConfirming = true;
        LastMessage = question + " (y/n)";
    }

    private Action<bool>? _answerAction;

    /// <summary>Asks a yes/no question whose answer, either way, goes to <paramref name="answer"/>.</summary>
    private void AskYesNo(string question, Action<bool> answer)
    {
        _answerAction = answer;
        IsConfirming = true;
        LastMessage = question + " (y/n)";
    }

    /// <summary>Answers a question asked by <see cref="AskFirst"/> or <see cref="AskYesNo"/>; false if none was pending.</summary>
    private bool ConfirmAction(bool yes)
    {
        if (_answerAction is { } answer)
        {
            _answerAction = null;
            answer(yes);
            return true;
        }
        if (_confirmAction is not { } action) return false;
        _confirmAction = null;
        if (yes) action();
        else LastMessage = "Cancelled.";
        return true;
    }

    /// <summary>'f': fire the first missile in the quiver, asking first if it says !f (or !*).</summary>
    private void FireDefault()
    {
        var bow = _game.Player.Inventory.Bow;
        var ammo = _game.Player.Inventory.Quiver.FirstOrDefault(q => bow is not null && q.Base.AmmoClass == bow.Base.AmmoClass);
        if (ammo is not null && Inscription.AsksFirst(ammo, 'f'))
            AskFirst($"Really fire {_game.Describe(ammo)}?", () => Execute(new FireCommand()));
        else Execute(new FireCommand());
    }
}
