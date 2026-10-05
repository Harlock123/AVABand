using Angband.Core.Geometry;

namespace Angband.Core.Game;

/// <summary>Pins a note on a square of the level (AVABand's own); an empty note takes the pin away. Takes no time.</summary>
public sealed record PinCommand(Loc At, string Text) : GameCommand;

// AVABand's own: map pins — "vault here", "come back with a pick" — a short note on a square, kept with
// the level (and its save), shown as a little flag on the map and the level map, and read out by Look and
// the mouse.
public sealed partial class GameSession
{
    /// <summary>The longest a pin's note may be.</summary>
    public const int MaxPinLength = 60;

    /// <summary>The note pinned on a square, if any.</summary>
    public string? PinAt(Loc at) => Level.Pins.GetValueOrDefault(at);

    private int Pin(Loc at, string text)
    {
        if (!Level.InBounds(at)) return 0;
        var note = text.Trim();
        if (note.Length > MaxPinLength) note = note[..MaxPinLength];
        if (note.Length == 0)
        {
            if (Level.Pins.Remove(at)) Publish(new MessageEvent("You take the pin away."));
            return 0;
        }
        Level.Pins[at] = note;
        Publish(new MessageEvent($"Pinned: \"{note}\"."));
        return 0;
    }
}
