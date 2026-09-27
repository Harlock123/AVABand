using Angband.Data.Tiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>Display options: ASCII or tiles, which tileset, tile scale and font size.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Scales offered for tiles; pixel art looks best at whole numbers.</summary>
    public static readonly double[] TileScales = [0.5, 0.75, 1, 1.5, 2, 3, 4];

    private readonly AppSettings _settings;
    private readonly Action<AppSettings>? _saveSettings;

    public IReadOnlyList<TilesetManifest> Tilesets { get; }

    public bool HasTilesets => Tilesets.Count > 0;

    /// <summary>A small fixed scene for previewing tilesets in the settings window.</summary>
    public IMapSource Preview { get; }

    [ObservableProperty] private double _mapFontSize = 16;
    [ObservableProperty] private bool _useTiles;
    [ObservableProperty] private TilesetManifest? _selectedTileset;
    [ObservableProperty] private double _tileScale = 2;

    public string DisplayModeText => UseTiles && SelectedTileset is { } t ? $"Tiles: {t.Name}" : "ASCII";

    partial void OnUseTilesChanged(bool value)
    {
        if (value && SelectedTileset is null)
        {
            UseTiles = false;
            AddMessage("No tilesets were found; staying in ASCII mode.");
            return;
        }
        SaveSettings();
    }

    partial void OnSelectedTilesetChanged(TilesetManifest? value) => SaveSettings();
    partial void OnTileScaleChanged(double value) => SaveSettings();
    partial void OnMapFontSizeChanged(double value) => SaveSettings();

    [RelayCommand]
    public void ToggleTiles() => UseTiles = !UseTiles;

    [RelayCommand]
    public void ZoomIn()
    {
        if (UseTiles) TileScale = TileScales.FirstOrDefault(s => s > TileScale + 0.01, TileScales[^1]);
        else MapFontSize = Math.Min(40, MapFontSize + 2);
    }

    [RelayCommand]
    public void ZoomOut()
    {
        if (UseTiles) TileScale = TileScales.LastOrDefault(s => s < TileScale - 0.01, TileScales[0]);
        else MapFontSize = Math.Max(8, MapFontSize - 2);
    }

    private void SaveSettings()
    {
        OnPropertyChanged(nameof(DisplayModeText));
        _settings.UseTiles = UseTiles;
        _settings.TilesetId = SelectedTileset?.Id ?? _settings.TilesetId;
        _settings.TileScale = TileScale;
        _settings.FontSize = MapFontSize;
        _saveSettings?.Invoke(_settings);
    }
}
