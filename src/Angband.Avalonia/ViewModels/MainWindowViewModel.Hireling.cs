using Angband.Core.Game;
using Angband.Core.Geometry;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// AVABand's hireling in the interface: their health in the sidebar under the monster health bar
/// ("[*******---] Bergil the archer (level 7, following)"), a green outline round them on the map,
/// and their orders — on keys, in the Game menu's Hireling submenu and on your own right-click menu.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private bool _hirelingVisible;
    [ObservableProperty] private string _hirelingStars = "";
    [ObservableProperty] private string _hirelingRest = "";
    [ObservableProperty] private string _hirelingName = "";
    [ObservableProperty] private IBrush _hirelingBrush = Brushes.White;

    /// <summary>Whether you have a hireling (the menu's orders are on only then).</summary>
    [ObservableProperty] private bool _hasHireling;

    /// <summary>Where your hireling stands, while you can see them: outlined in green on the map.</summary>
    public Loc? Ally => _game.HirelingHere is { IsVisible: true } body && !_game.IsHallucinating ? body.Position : null;

    private void UpdateHirelingBar()
    {
        HasHireling = _game.Hireling is not null;
        if (_game.HirelingHealthBar() is not { } bar)
        {
            HirelingVisible = false;
            return;
        }
        HirelingVisible = true;
        HirelingName = bar.Name;
        HirelingStars = bar.Unknown ? "" : new string('*', bar.Stars);
        HirelingRest = new string('-', 10 - (bar.Unknown ? 0 : bar.Stars));
        HirelingBrush = new ImmutableSolidColorBrush(Color.FromUInt32(_cells.Color(bar.Color)));
    }

    /// <summary>Game → Hireling → Follow close.</summary>
    [RelayCommand]
    private void HirelingFollow() => Execute(new HirelingOrderCommand(HirelingOrder.Follow));

    /// <summary>Game → Hireling → Hold here.</summary>
    [RelayCommand]
    private void HirelingHold() => Execute(new HirelingOrderCommand(HirelingOrder.Hold));

    /// <summary>Game → Hireling → Attack my target.</summary>
    [RelayCommand]
    private void HirelingAttack() => Execute(new HirelingOrderCommand(HirelingOrder.Attack));

    /// <summary>Your right-click menu's orders for the hireling (and, in town, sending them home).</summary>
    private List<(string, Action)> HirelingMenu()
    {
        var menu = new List<(string, Action)>
        {
            ("Follow close", HirelingFollow),
            ("Hold here", HirelingHold),
            ("Attack my target", HirelingAttack),
        };
        if (_game.Player.Depth == 0) menu.Add(("Send them home", () => Execute(new HirelingOrderCommand(HirelingOrder.Dismiss))));
        return menu;
    }
}
