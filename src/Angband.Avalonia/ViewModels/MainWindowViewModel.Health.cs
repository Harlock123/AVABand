using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The sidebar's monster health bar (Angband prt_health): "[*******---]" in the tracked monster's
/// colour, with its name; dashes alone while it can't be seen.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private bool _healthVisible;
    [ObservableProperty] private string _healthStars = "";
    [ObservableProperty] private string _healthRest = "";
    [ObservableProperty] private string _healthName = "";
    [ObservableProperty] private IBrush _healthBrush = Brushes.White;

    private void UpdateHealthBar()
    {
        if (_game.HealthBarFor() is not { } bar)
        {
            HealthVisible = false;
            return;
        }
        HealthVisible = true;
        HealthName = bar.Name;
        HealthStars = new string('*', bar.Stars);
        HealthRest = new string('-', 10 - bar.Stars);
        HealthBrush = new ImmutableSolidColorBrush(Color.FromUInt32(_cells.Color(bar.Color)));
    }
}
