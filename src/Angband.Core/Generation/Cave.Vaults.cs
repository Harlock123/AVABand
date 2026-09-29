using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

// Angband gen-room.c's vaults and room templates, gen-chunk.c's symmetry transforms, and
// gen-monster.c's monster choosing for vaults, chambers, lairs and gauntlets.
public sealed partial class Cave
{
    private const int SymtrMaxWeight = 1024;

    /// <summary>Angband symmetry_transform: rotate (clockwise quarter turns), reflect, then translate.</summary>
    internal static Loc SymmetryTransform(Loc g, int y0, int x0, int height, int width, int rotate, bool reflect)
    {
        int rheight = height, rwidth = width;
        for (var i = 0; i < rotate % 4; i++)
        {
            g = new Loc(rheight - 1 - g.Y, g.X);
            (rwidth, rheight) = (rheight, rwidth);
        }
        if (reflect) g = new Loc(rwidth - 1 - g.X, g.Y);
        return new Loc(g.X + x0, g.Y + y0);
    }

    /// <summary>Angband calc_default_transpose_weight: tall templates are likelier to be turned on their side.</summary>
    private static int CalcDefaultTransposeWeight(int height, int width) =>
        SymtrMaxWeight / 64 * Math.Max(0, Math.Min(64, 128 * height / width - 64));

    /// <summary>Angband get_random_symmetry_transform (no constraints): the rotation and reflection, and the size after.</summary>
    private (int Rotate, bool Reflect, int Height, int Width) RandomSymmetryTransform(int height, int width, int transposeWeight)
    {
        transposeWeight = Math.Clamp(transposeWeight, 0, SymtrMaxWeight);
        var weights = new int[9];
        weights[1] = weights[0] + SymtrMaxWeight;
        weights[2] = weights[1] + transposeWeight;
        weights[3] = weights[2] + SymtrMaxWeight;
        weights[4] = weights[3] + transposeWeight;
        weights[5] = weights[4] + SymtrMaxWeight;
        weights[6] = weights[5] + transposeWeight;
        weights[7] = weights[6] + SymtrMaxWeight;
        weights[8] = weights[7] + transposeWeight;
        var draw = _rng.RandInt0(weights[8]);
        int low = 0, high = 8;
        while (low != high - 1)
        {
            var mid = (low + high) / 2;
            if (weights[mid] <= draw) low = mid;
            else high = mid;
        }
        var rotate = low % 4;
        var turned = rotate is 1 or 3;
        return (rotate, low >= 4, turned ? width : height, turned ? height : width);
    }

    /// <summary>Angband random_vault: one of the type for this depth, each as likely as another.</summary>
    private VaultDef? RandomVault(int depth, string type)
    {
        VaultDef? r = null;
        var n = 1;
        foreach (var v in _data.Vaults)
        {
            if (v.Type != type || v.MinDepth > depth || v.MaxDepth < depth) continue;
            if (_rng.OneIn(n)) r = v;
            n++;
        }
        return r;
    }

    /// <summary>Angband random_room_template.</summary>
    private RoomTemplateDef? RandomRoomTemplate(int type, int rating)
    {
        RoomTemplateDef? r = null;
        var n = 1;
        foreach (var t in _data.RoomTemplates)
        {
            if (t.Type != type || t.Rating != rating) continue;
            if (_rng.OneIn(n)) r = t;
            n++;
        }
        return r;
    }

    /// <summary>Angband build_template: a room template of the profile's rating (type 1).</summary>
    private bool BuildTemplate(Level c, Loc centre, int rating) =>
        RandomRoomTemplate(1, rating) is { } room && BuildRoomTemplate(c, centre, room);

    /// <summary>
    /// Angband build_room_template: the template's walls, doors (one numbered set chosen), optional
    /// walls (all or none), traps and treasure, then guards and treasure about the 8s and 9s.
    /// </summary>
    private bool BuildRoomTemplate(Level c, Loc centre, RoomTemplateDef room)
    {
        var ymax = room.Height;
        var xmax = room.Width;
        var light = c.Depth <= _rng.RandInt1(25);
        var rnddoors = _rng.RandInt1(Math.Max(1, room.Doors));
        var rndwalls = _rng.OneIn(2);
        (int Rotate, bool Reflect, int Height, int Width) tr;
        if (OutsideChunk(c, centre))
        {
            tr = RandomSymmetryTransform(ymax, xmax, CalcDefaultTransposeWeight(ymax, xmax));
            if (!FindSpace(ref centre, tr.Height + 2, tr.Width + 2)) return false;
        }
        else tr = RandomSymmetryTransform(ymax, xmax, 0);
        var x0 = centre.X - tr.Width / 2;
        var y0 = centre.Y - tr.Height / 2;
        var fewEntrances = room.Has("FEW_ENTRANCES");

        for (var dy = 0; dy < ymax; dy++)
        for (var dx = 0; dx < xmax; dx++)
        {
            var t = room.Rows[dy][dx];
            var g = SymmetryTransform(new Loc(dx, dy), y0, x0, ymax, xmax, tr.Rotate, tr.Reflect);
            if (t == ' ') continue;
            SetFeat(c, g, _f.Floor);
            switch (t)
            {
                case '%':
                    SetMarkedGranite(c, g, SquareFlags.WallOuter);
                    if (fewEntrances) AppendEntrance(g);
                    break;
                case '#': SetMarkedGranite(c, g, SquareFlags.WallSolid); break;
                case '+': PlaceClosedDoor(c, g); break;
                case '^': if (_rng.OneIn(4)) PlaceTrap(c, g, c.Depth); break;
                case 'x': if (rndwalls) SetMarkedGranite(c, g, SquareFlags.WallSolid); break;
                case '(': if (rndwalls) PlaceSecretDoor(c, g); break;
                case ')':
                    if (!rndwalls) PlaceSecretDoor(c, g);
                    else SetMarkedGranite(c, g, SquareFlags.WallSolid);
                    break;
                case '8':
                    if (_rng.RandInt0(100) < 80 || _dun.Persist) PlaceObject(c, g, c.Depth, false, false);
                    else PlaceRandomStairs(c, g, _dun.Quest);
                    break;
                case '[': PlaceObject(c, g, c.Depth, false, false, room.Tval); break;
                case >= '1' and <= '6':
                    if (t - '0' == rnddoors) PlaceSecretDoor(c, g);
                    else SetMarkedGranite(c, g, SquareFlags.WallSolid);
                    break;
            }
            SqOn(c, g, SquareFlags.Room);
            if (light) SqOn(c, g, SquareFlags.Glow);
        }

        for (var dy = 0; dy < ymax; dy++)
        for (var dx = 0; dx < xmax; dx++)
        {
            var t = room.Rows[dy][dx];
            var g = SymmetryTransform(new Loc(dx, dy), y0, x0, ymax, xmax, tr.Rotate, tr.Reflect);
            switch (t)
            {
                case '#':
                    if (IsWallSolid(c, g) && CountNeighbors(c, g, (l, n) => IsRoom(l, n)) == 8)
                    {
                        SqOff(c, g, SquareFlags.WallSolid);
                        SqOn(c, g, SquareFlags.WallInner);
                    }
                    break;
                case '8':
                    VaultMonsters(c, g, c.Depth + 2, _rng.RandInt0(2) + 3);
                    break;
                case '9':
                {
                    var off2 = new Loc(2, -2);
                    var off3 = new Loc(3, 3);
                    VaultMonsters(c, g - off3, c.Depth + _rng.RandInt0(2), _rng.RandInt1(2));
                    VaultMonsters(c, g + off3, c.Depth + _rng.RandInt0(2), _rng.RandInt1(2));
                    if (_rng.OneIn(2)) VaultObjects(c, g + off2, c.Depth, 1 + _rng.RandInt0(2));
                    if (_rng.OneIn(2)) VaultObjects(c, g - off2, c.Depth, 1 + _rng.RandInt0(2));
                    break;
                }
            }
        }
        return true;
    }

    /// <summary>Angband build_vault_type: a vault of the type for this depth, its rating added to the level's.</summary>
    private bool BuildVaultType(Level c, Loc centre, string type)
    {
        if (RandomVault(c.Depth, type) is not { } v) return false;
        if (!BuildVault(c, centre, v)) return false;
        AddToMonsterRating(c, v.Rating);
        c.Vaults.Add(v.Name);
        return true;
    }

    /// <summary>
    /// Angband help_greater_vault: only as the first room; a depth-scaled chance (a third at level
    /// 90 and below, two ninths in the 80s...), cut to a third again outside the classic profile.
    /// </summary>
    private bool HelpGreaterVault(Level c, Loc centre, string name)
    {
        if (_dun.CentN - _dun.NStairRoom > (OutsideChunk(c, centre) ? 0 : 1)) return false;
        int numerator = 1, denominator = 3;
        for (var i = 90; i > c.Depth; i -= 10)
        {
            numerator *= 2;
            denominator *= 3;
        }
        if (_rng.RandInt0(denominator) >= numerator) return false;
        if (_dun.Profile.Name != "classic" && !_rng.OneIn(3)) return false;
        return BuildVaultType(c, centre, name);
    }

    /// <summary>
    /// Angband build_vault: lay down the vault (turned and reflected at random) — walls, veins,
    /// rubble, secret doors, traps, stairs, lava — then its monsters and treasure by symbol, and
    /// its letters' monsters. Every grid but the outer wall is part of the vault; no random
    /// monsters are placed in it.
    /// </summary>
    private bool BuildVault(Level c, Loc centre, VaultDef v)
    {
        (int Rotate, bool Reflect, int Height, int Width) tr;
        if (OutsideChunk(c, centre))
        {
            tr = RandomSymmetryTransform(v.Height, v.Width, CalcDefaultTransposeWeight(v.Height, v.Width));
            if (!FindSpace(ref centre, tr.Height + 2, tr.Width + 2)) return false;
        }
        else tr = RandomSymmetryTransform(v.Height, v.Width, 0);
        var x0 = centre.X - tr.Width / 2;
        var y0 = centre.Y - tr.Height / 2;
        int y1 = y0, x1 = x0, y2 = y0 + tr.Height - 1, x2 = x0 + tr.Width - 1;
        if (!InBounds(c, new Loc(x1, y1)) || !InBounds(c, new Loc(x2, y2))) return false;
        GenerateMark(c, y1, x1, y2, x2, SquareFlags.MonRestrict);
        var fewEntrances = v.Has("FEW_ENTRANCES");

        for (var y = 0; y < v.Height; y++)
        for (var x = 0; x < v.Width; x++)
        {
            var t = v.Rows[y][x];
            if (t == ' ') continue;
            var g = SymmetryTransform(new Loc(x, y), y0, x0, v.Height, v.Width, tr.Rotate, tr.Reflect);
            SetFeat(c, g, _f.Floor);
            var icky = true;
            switch (t)
            {
                case '%':
                    SetMarkedGranite(c, g, SquareFlags.WallOuter);
                    if (fewEntrances) AppendEntrance(g);
                    icky = false;
                    break;
                case '#': SetMarkedGranite(c, g, SquareFlags.WallSolid); break;
                case '@': SetFeat(c, g, _f.Permanent); break;
                case '*': SetFeat(c, g, _rng.OneIn(2) ? _f.MagmaTreasure : _f.QuartzTreasure); break;
                case ':': SetFeat(c, g, _rng.OneIn(2) ? _f.PassableRubble : _f.Rubble); break;
                case '+': PlaceSecretDoor(c, g); break;
                case '^': if (_rng.OneIn(4)) PlaceTrap(c, g, c.Depth); break;
                case '&':
                    if (_rng.RandInt0(100) < 75) PlaceObject(c, g, c.Depth, false, false);
                    else if (_rng.OneIn(4)) PlaceTrap(c, g, c.Depth);
                    break;
                case '<':
                    if (!_dun.Persist) SetFeat(c, g, _f.UpStair);
                    break;
                case '>':
                    if (_dun.Persist) break;
                    SetFeat(c, g, _dun.Quest || c.Depth >= _data.Constants.MaxDepth ? _f.UpStair : _f.DownStair);
                    break;
                case '`': SetFeat(c, g, _f.Lava); break;
            }
            SqOn(c, g, SquareFlags.Room);
            if (icky) SqOn(c, g, SquareFlags.Vault);
        }

        var racialSymbols = new List<char>();
        for (var y = 0; y < v.Height; y++)
        for (var x = 0; x < v.Width; x++)
        {
            var t = v.Rows[y][x];
            if (t == ' ') continue;
            var g = SymmetryTransform(new Loc(x, y), y0, x0, v.Height, v.Width, tr.Rotate, tr.Reflect);
            if (char.IsAsciiLetter(t) && t != 'x' && t != 'X')
            {
                if (!racialSymbols.Contains(t) && racialSymbols.Count < 30) racialSymbols.Add(t);
                continue;
            }
            switch (t)
            {
                case '1':
                    if (_rng.OneIn(2)) PickAndPlaceMonster(c, g, c.Depth, true, true);
                    else if (_rng.OneIn(2)) PlaceObject(c, g, c.Depth, _rng.OneIn(8), false);
                    else if (_rng.OneIn(4)) PlaceTrap(c, g, c.Depth);
                    break;
                case '2': PickAndPlaceMonster(c, g, c.Depth + 5, true, true); break;
                case '3': PlaceObject(c, g, c.Depth + 3, false, false); break;
                case '4':
                    if (_rng.OneIn(2)) PickAndPlaceMonster(c, g, c.Depth + 3, true, true);
                    if (_rng.OneIn(2)) PlaceObjectUnder(c, g, c.Depth + 7, false, false);
                    break;
                case '5': PlaceObject(c, g, c.Depth + 7, false, false); break;
                case '6': PickAndPlaceMonster(c, g, c.Depth + 11, true, true); break;
                case '7': PlaceObject(c, g, c.Depth + 15, false, false); break;
                case '0': PickAndPlaceMonster(c, g, c.Depth + 20, true, true); break;
                case '9':
                    PickAndPlaceMonster(c, g, c.Depth + 9, true, true);
                    PlaceObjectUnder(c, g, c.Depth + 7, true, false);
                    break;
                case '8':
                    PickAndPlaceMonster(c, g, c.Depth + 40, true, true);
                    PlaceObjectUnder(c, g, c.Depth + 20, true, true);
                    break;
                case '~': PlaceObject(c, g, c.Depth + 5, false, false, "chest"); break;
                case '$': PlaceGold(c, g, c.Depth); break;
                case ']':
                {
                    var temp = _rng.OneIn(3) ? _rng.RandInt1(9) : _rng.RandInt1(8);
                    var tval = temp switch
                    {
                        1 => "boots", 2 => "gloves", 3 => "helm", 4 => "crown", 5 => "shield", 6 => "cloak",
                        7 => "soft_armour", 8 => "hard_armour", _ => "dragon_armour",
                    };
                    PlaceObject(c, g, c.Depth + 3, true, false, tval);
                    break;
                }
                case '|':
                {
                    var tval = _rng.RandInt1(4) switch { 1 => "sword", 2 => "polearm", 3 => "hafted", _ => "bow" };
                    PlaceObject(c, g, c.Depth + 3, true, false, tval);
                    break;
                }
                case '=': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "ring"); break;
                case '"': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "amulet"); break;
                case '!': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "potion"); break;
                case '?': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "scroll"); break;
                case '_': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "staff"); break;
                case '-': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, _rng.OneIn(2) ? "wand" : "rod"); break;
                case ',': PlaceObject(c, g, c.Depth + 3, _rng.OneIn(4), false, "food"); break;
                case '#':
                    if (IsWallSolid(c, g) && CountNeighbors(c, g, (l, n) => IsRoom(l, n)) == 8)
                    {
                        SqOff(c, g, SquareFlags.WallSolid);
                        SqOn(c, g, SquareFlags.WallInner);
                    }
                    break;
                case '@':
                    if (CountNeighbors(c, g, (l, n) => IsRoom(l, n)) == 8) SqOn(c, g, SquareFlags.WallInner);
                    break;
            }
        }

        GetVaultMonsters(c, racialSymbols, v, y0, x0, tr.Rotate, tr.Reflect);
        return true;
    }

    /// <summary>
    /// An object that lies with a monster on its grid (Angband places the monster, then the object,
    /// on the same vault grid).
    /// </summary>
    private void PlaceObjectUnder(Level c, Loc g, int level, bool good, bool great)
    {
        if (!InBounds(c, g) || !Feat(c, g).Has(TerrainFlags.Object) || HasTrap(c, g)) return;
        if (c.SpawnHints.Any(h => h.Loc == g && h.Kind is SpawnKind.Object or SpawnKind.GoodObject or SpawnKind.GreatObject or SpawnKind.Gold))
            return;
        var kind = great ? SpawnKind.GreatObject : good ? SpawnKind.GoodObject : SpawnKind.Object;
        c.SpawnHints.Add(new SpawnHint(g, kind, level - c.Depth));
        Occupied(c).Add(g);
    }

    /// <summary>
    /// Angband get_vault_monsters: for each letter, monsters of that symbol (awake, alone) at the
    /// vault type's depth — the level's in an interesting room, +2/+4/+6 in lesser/medium/greater
    /// vaults. (4.2.5 walks the untransformed layout here; the letters' own grids are used.)
    /// </summary>
    private void GetVaultMonsters(Level c, List<char> racialSymbols, VaultDef v, int y0, int x0, int rotate, bool reflect)
    {
        var depth = c.Depth + (v.Type.Contains("Lesser vault") ? 2 : v.Type.Contains("Medium vault") ? 4
            : v.Type.Contains("Greater vault") ? 6 : 0);
        foreach (var symbol in racialSymbols)
            for (var y = 0; y < v.Height; y++)
            for (var x = 0; x < v.Width; x++)
            {
                if (v.Rows[y][x] != symbol) continue;
                var g = SymmetryTransform(new Loc(x, y), y0, x0, v.Height, v.Width, rotate, reflect);
                PickAndPlaceMonster(c, g, depth, false, false, $"base:{symbol},uniques");
            }
    }

    /// <summary>
    /// Angband get_chamber_monsters: a theme (usually a pit profile near the depth, one time in
    /// twenty a random monster base) and about one monster per 20-30 grids, two in three asleep.
    /// </summary>
    private void GetChamberMonsters(Level c, int y1, int x1, int y2, int x2, int area)
    {
        var random = _rng.OneIn(20);
        var depth = c.Depth + _rng.RandInt0(11) - 5;
        if (!random) SetPitType(depth, 0);
        depth = c.Depth + (c.Depth < 60 ? c.Depth / 12 : 5);

        string restriction;
        if (random)
        {
            if (RandomBase(depth, c.Depth) is not { } symbol) return;
            restriction = $"base:{symbol},uniques";
        }
        else
        {
            if (_dun.PitType is not { } pit) return;
            restriction = $"pit:{pit.Id}";
        }

        GenerateMark(c, y1, x1, y2, x2, SquareFlags.MonRestrict);
        var left = area / (30 - c.Depth / 10);
        for (var i = 0; i < 300 && left > 0; i++)
        {
            var g = new Loc(x1 + _rng.RandInt0(1 + Math.Abs(x2 - x1)), y1 + _rng.RandInt0(1 + Math.Abs(y2 - y1)));
            if (!IsEmpty(c, g)) continue;
            PickAndPlaceMonster(c, g, c.Depth, _rng.RandInt0(3) != 0, false, restriction);
            left--;
        }
    }

    /// <summary>
    /// Angband mon_restrict("random"): the symbol of a random race's base — near the depth at
    /// first, any in depth after 200 tries.
    /// </summary>
    private char? RandomBase(int depth, int currentDepth)
    {
        var races = _data.Monsters;
        for (var i = 0; i < 2500; i++)
        {
            var r = races[_rng.RandInt0(races.Count)];
            if (r.Rarity <= 0 || r.IsUnique || r.Depth == 0 || r.Depth > depth) continue;
            if (i < 200 && Math.Abs(r.Depth - currentDepth) >= 1 + currentDepth / 4) continue;
            return _data.MonsterBases.FirstOrDefault(b => b.Id == r.Base)?.Glyph[0] ?? r.Glyph;
        }
        return null;
    }

    /// <summary>
    /// Angband spread_monsters: up to <paramref name="num"/> monsters of a pit theme scattered in a
    /// rectangle about a centre (asleep, with groups).
    /// </summary>
    private void SpreadMonsters(Level c, PitProfileDef pit, int depth, int num, int y0, int x0, int dy, int dx)
    {
        var count = 0;
        for (var i = 0; count < num && i < 50; i++)
        {
            Loc g = default;
            var found = false;
            for (var j = 0; j < 10; j++)
            {
                g = new Loc(_rng.Spread(x0, dx), _rng.Spread(y0, dy));
                if (!InBounds(c, g)) continue;
                found = true;
                break;
            }
            if (!found) return;
            if (!IsEmpty(c, g)) continue;
            PickAndPlaceMonster(c, g, depth, true, true, $"pit:{pit.Id}");
            count++;
            i = 0;
        }
    }
}
