using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Media;

namespace Angband.Avalonia.Rendering;

/// <summary>Draws individual map cells. <c>MapView</c> handles the camera and asks for each visible cell.</summary>
public interface IMapRenderer
{
    /// <summary>Size of one map cell in device-independent pixels.</summary>
    Size CellSize { get; }

    void DrawCell(DrawingContext context, Rect dest, in MapCell cell);
}
