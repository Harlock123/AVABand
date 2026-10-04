using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Avalonia.ViewModels;

/// <summary>A part of a shop's front in town: its walls (tinted its colour), its windows (with its wares), or its door.</summary>
public enum FacadeKind { Wall, Window, Door }

/// <summary>
/// A square of a shopfront (AVABand's own): which part, the shop's colour (ARGB), which way the street
/// lies from it (the awning hangs over that side), and, in a window, a ware to show.
/// </summary>
public sealed record ShopFacade(FacadeKind Kind, string ShopId, uint Colour, int StreetX, int StreetY, MapCell? Ware = null);

/// <summary>A shop's name over its door: where the door is, which way the street lies, and the shop's colour.</summary>
public sealed record ShopSign(Loc Door, string Name, uint Colour, int StreetX, int StreetY);

// AVABand's own: the town's shopfronts. Each building is tinted its shop's colour; the wall squares either
// side of the door become windows showing its wares (drawn as the tileset or the letters draw them), with
// a striped awning over the street side; a lantern burns by each door at night; the shop's name hangs
// over its door; and in letters, the shop's number stands on its colour. Worked out from the town as it
// is (a door, its street, the block of wall around it), so it suits any town the game builds.
public sealed partial class MainWindowViewModel
{
    /// <summary>Each shop's colour, and the wares in its windows (object kinds; none for the Home, which has curtains).</summary>
    private static readonly Dictionary<string, (uint Colour, string[] Wares)> ShopLooks = new()
    {
        ["general"] = (0xFFB08040, ["flask_of_oil", "ration_of_food"]),
        ["armoury"] = (0xFF7D93AB, ["leather_shield", "chain_mail"]),
        ["weaponsmith"] = (0xFFC04040, ["long_sword", "dagger"]),
        ["bookseller"] = (0xFF4A64C8, ["first_spells", "novices_handbook"]),
        ["alchemist"] = (0xFF3FA65A, ["cure_light_wounds", "phase_door"]),
        ["magic"] = (0xFF2FB3C8, ["wand_of_magic_missile", "ring_of_protection"]),
        ["black_market"] = (0xFF6A6A78, ["amulet_of_slow_digestion", "emerald"]),
        ["home"] = (0xFFD09030, []),
        ["inn"] = (0xFF6AA04A, ["pint_of_fine_wine", "flask_of_whisky"]),
        ["artificer"] = (0xFF9A5AD0, ["ruby", "sapphire"]),
    };

    private Level? _facadeLevel;
    private Dictionary<Loc, ShopFacade> _facades = [];
    private List<ShopSign> _shopSigns = [];

    /// <summary>The shopfront part on a square in town, if any (with the option on).</summary>
    public ShopFacade? FacadeAt(int x, int y) =>
        OptionValue(DisplayOptions.Shopfronts) && Facades().TryGetValue(new Loc(x, y), out var part) ? part : null;

    /// <summary>The shops' names over their doors (with the option on).</summary>
    public IReadOnlyList<ShopSign> ShopSigns => OptionValue(DisplayOptions.ShopNames) && _game.Level.Depth == 0 ? Signs() : [];

    /// <summary>Night in town: the shops' windows glow and their lanterns burn.</summary>
    public bool IsNight => _game.Level.Depth == 0 && !_game.IsDaytime;

    private IReadOnlyList<ShopSign> Signs()
    {
        Facades();
        return _shopSigns;
    }

    /// <summary>Works the shopfronts out for this town (once per level).</summary>
    private Dictionary<Loc, ShopFacade> Facades()
    {
        var level = _game.Level;
        if (ReferenceEquals(level, _facadeLevel)) return _facades;
        _facadeLevel = level;
        _facades = [];
        _shopSigns = [];
        if (level.Depth != 0) return _facades;

        foreach (var door in level.AllLocs())
        {
            if (level.FeatureAt(door).Shop is not { } shopId) continue;
            var look = ShopLooks.GetValueOrDefault(shopId, (0xFF8A8A8A, []));
            // The street: the open square beside the door.
            var street = new[] { new Loc(0, 1), new Loc(0, -1), new Loc(1, 0), new Loc(-1, 0) }
                .FirstOrDefault(d => level.InBounds(door + d) && level.IsPassable(door + d) && level.FeatureAt(door + d).Shop is null);
            if (street == default) continue;

            // The building: the block of permanent wall the door is set in.
            var building = new HashSet<Loc>();
            var queue = new Queue<Loc>(level.Neighbors(door).Where(n => level.IsPermanent(n) && level.InBoundsFully(n)));
            while (queue.Count > 0 && building.Count < 400)
            {
                var p = queue.Dequeue();
                if (!building.Add(p)) continue;
                foreach (var n in level.Neighbors(p))
                    if (!building.Contains(n) && level.IsPermanent(n) && level.InBoundsFully(n) && level.FeatureAt(n).Shop is null) queue.Enqueue(n);
            }
            if (building.Count >= 400) building.Clear(); // (not a building: the town's own wall)
            foreach (var p in building) _facades[p] = new ShopFacade(FacadeKind.Wall, shopId, look.Colour, street.X, street.Y);

            // Windows: the wall squares either side of the door, along the front, showing the shop's wares.
            var along = new Loc(street.Y, street.X);
            var wares = look.Wares.Select(id => _data.Object(id) is { } kind && _data.ObjectBase(kind.Base) is { } b
                ? _cells.ObjectKind(kind, b, MapCell.Unknown) : (MapCell?)null).OfType<MapCell>().ToList();
            var i = 0;
            foreach (var step in new[] { -1, 1, -2, 2 })
            {
                var w = new Loc(door.X + along.X * step, door.Y + along.Y * step);
                if (!building.Contains(w) || !level.IsPassable(w + street)) continue; // (only on the street side)
                _facades[w] = new ShopFacade(FacadeKind.Window, shopId, look.Colour, street.X, street.Y, wares.Count == 0 ? null : wares[i++ % wares.Count]);
            }
            _facades[door] = new ShopFacade(FacadeKind.Door, shopId, look.Colour, street.X, street.Y);
            var name = _data.Shops.FirstOrDefault(s => s.Id == shopId)?.Name ?? shopId;
            _shopSigns.Add(new ShopSign(door, name, look.Colour, street.X, street.Y));
        }
        return _facades;
    }

    /// <summary>In letters, a shop's number stands on its colour (darkened), in white.</summary>
    private MapCell ShopDoorLetters(MapCell cell, Loc p)
    {
        if (!OptionValue(DisplayOptions.Shopfronts) || _game.Level.Depth != 0
            || !Facades().TryGetValue(p, out var part) || part.Kind != FacadeKind.Door) return cell;
        var c = part.Colour;
        uint Dark(int shift) => (uint)(((c >> shift) & 0xFF) * 45 / 100) << shift;
        return cell with { Background = 0xFF000000 | Dark(16) | Dark(8) | Dark(0), Foreground = 0xFFFFF4D8 };
    }
}
