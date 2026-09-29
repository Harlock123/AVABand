using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;
using Angband.Data.Tiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IMapSource
{
    private const int MaxMessages = 50;
    private readonly GameData _data;
    private readonly MapCellBuilder _cells;
    private GameSession _game = null!;

    public MainWindowViewModel(GameData data)
        : this(data, [], new AppSettings(), save: null)
    {
    }

    public MainWindowViewModel(GameData data, IReadOnlyList<TilesetManifest> tilesets, AppSettings settings,
        Action<AppSettings>? save)
    {
        _data = data;
        _cells = new MapCellBuilder(data);
        Tilesets = tilesets;
        _settings = settings;
        _saveSettings = save;
        _mapFontSize = settings.FontSize;
        _tileScale = settings.TileScale;
        _interfaceScale = Math.Clamp(settings.InterfaceScale, 0.8, 2.0);
        Views.DialogFit.InterfaceScale = _interfaceScale;
        _selectedTileset = tilesets.FirstOrDefault(t => t.Id == settings.TilesetId) ?? tilesets.FirstOrDefault();
        _useTiles = settings.UseTiles && _selectedTileset is not null;
        _showMonsterPanel = settings.ShowMonsterPanel;
        _showRecallPanel = settings.ShowRecallPanel;
        _showHotbar = settings.ShowHotbar;
        _showObjectPanel = settings.ShowObjectPanel;
        Preview = new PreviewMapSource(data, _cells);
        RefreshBindingRows();
        StartGame((ulong)Environment.TickCount64);
    }

    /// <summary>Bumped whenever the map changes, so the map view knows to redraw.</summary>
    [ObservableProperty] private int _revision;
    [ObservableProperty] private string _statusText = "";

    /// <summary>
    /// The status line in its fields ("HP 20/20  |", "Exp 0/10  |"…), so the bar wraps between fields,
    /// never inside one, when it doesn't fit (a big interface size, a narrow window).
    /// </summary>
    public IReadOnlyList<string> StatusFields { get; private set; } = [];

    partial void OnStatusTextChanged(string value)
    {
        var fields = value.Split("  |  ");
        StatusFields = [.. fields.Select((f, i) => i < fields.Length - 1 ? f + "  |" : f)];
        OnPropertyChanged(nameof(StatusFields));
    }
    [ObservableProperty] private string _lastMessage = "";

    public ObservableCollection<string> Messages { get; } = [];

    public IMapSource Map => this;

    public int Width => _game.Level.Width;
    public int Height => _game.Level.Height;
    /// <summary>The camera follows the player, or a free-roaming cursor.</summary>
    public Loc Focus => IsLocating ? _locateCentre : IsLooking && _cursorFree ? _cursor : _game.Player.Position;

    public MapCell GetCell(int x, int y)
    {
        var p = new Loc(x, y);
        var level = _game.Level;
        var terrain = TerrainCell(p);

        if (_game.Player.Position == p) return _cells.Player(terrain, _game.Player.Hp, _game.Player.MaxHp);
        if (level.Monsters.At(p) is { } monster && (monster.IsVisible || monster.IsDetected || ShowWholeMap))
            return _game.IsHallucinating
                ? _cells.Monster(Hallucination(_data.Monsters, p), terrain)
                : _cells.Monster(monster.Race, terrain, MapCellBuilder.HasRareLook(monster.Id, level.Seed));
        if (terrain.IsUnknown) return terrain;

        ref var sq = ref level[x, y];
        if (ShowWholeMap || sq.Has(SquareFlags.Seen))
        {
            // Ignored objects aren't shown (Angband); 'K' brings them back.
            var pile = level.Objects.At(p).Where(i => !Hidden(i)).ToList();
            if (pile.Count > 0 && _game.IsHallucinating && Hallucination(_data.Objects, p) is var fake
                && _data.ObjectBase(fake.Base) is { } fakeBase)
                return _cells.ObjectKind(fake, fakeBase, terrain);
            if (pile.Count > 0) return _cells.Object(pile[0], pile.Count, _game.Knowledge, terrain);
            if (_game.ObjectShownAt(p) is { } disguise && !Hidden(disguise)) return _cells.Object(disguise, 1, _game.Knowledge, terrain); // a mimic
        }
        else if (_game.Known.RememberedObject(p) is { } remembered && !Hidden(remembered))
            return _cells.Object(remembered, 1, _game.Knowledge, terrain);

        if (sq.Trap != 0 && (ShowWholeMap || sq.Has(SquareFlags.TrapVisible)) && _data.TrapByIndex(sq.Trap) is { } trap)
            return _cells.Trap(trap, terrain);
        return terrain;
    }

    /// <summary>
    /// Angband hallucinatory_monster / hallucinatory_object: while hallucinating, each monster and
    /// object seen looks like a random one — a new one every turn (not every repaint), and chosen
    /// without touching the game's random numbers.
    /// </summary>
    private T Hallucination<T>(IReadOnlyList<T> all, Loc p)
    {
        var hash = HashCode.Combine(p.X, p.Y, _game.NormalTurns);
        return all[(int)((uint)hash % (uint)all.Count)];
    }

    /// <summary>
    /// Angband display_map's priority for a square in the overview map: the player above all, then
    /// anything drawn over the terrain, then the terrain's own display priority.
    /// </summary>
    public int OverviewPriority(Loc p, MapCell cell)
    {
        if (p == _game.Player.Position) return int.MaxValue;
        var level = _game.Level;
        var seen = ShowWholeMap || level[p].Has(SquareFlags.Seen);
        var feature = level.Terrain[seen ? level[p].Feature : _game.Known.Feature(p)];
        if (feature.Mimic is { } mimic) feature = level.Terrain[mimic];
        return cell.UnderKey is not null ? Math.Max(OverviewMapSource.OnTopPriority, feature.Priority) : feature.Priority;
    }

    /// <summary>'M': the whole level at a glance.</summary>
    public event Action<OverviewMapSource>? OverviewRequested;

    [RelayCommand]
    public void ShowOverviewMap() => OverviewRequested?.Invoke(new OverviewMapSource(this));

    /// <summary>What the player knows of the terrain: live when seen, memory otherwise, blank if unknown.</summary>
    private MapCell TerrainCell(Loc p)
    {
        var level = _game.Level;
        ref var sq = ref level[p];
        var seen = ShowWholeMap || sq.Has(SquareFlags.Seen);
        if (!seen && !_game.Known.IsKnown(p)) return MapCell.Unknown;

        var feature = level.Terrain[seen ? sq.Feature : _game.Known.Feature(p)];
        if (feature.Mimic is { } mimic) feature = level.Terrain[mimic]; // undiscovered secret doors

        var lighting = !seen ? TileLighting.Dark
            : !sq.Has(SquareFlags.Glow) && sq.Has(SquareFlags.Lit) ? TileLighting.Torch
            : TileLighting.Lit;
        return _cells.Terrain(feature, lighting);
    }

    /// <summary>Light and shadow on the map (the option): torchlight fading from you, remembered squares dimmer.</summary>
    public bool LightAndShadow => OptionValue(DisplayOptions.LightAndShadow) && !ShowWholeMap;

    /// <summary>
    /// How dark a square is drawn: lit rooms hardly at all; squares lit only by your own light more so
    /// the further they are from you (they flicker, and are warmed near you); squares you remember but
    /// can't see now, darker still. Unknown squares are black anyway.
    /// </summary>
    public MapShade ShadeAt(int x, int y)
    {
        var level = _game.Level;
        ref var sq = ref level[x, y];
        if (!sq.Has(SquareFlags.Seen)) return _game.Known.IsKnown(new Loc(x, y)) ? new MapShade(0.38f) : default;
        if (sq.Has(SquareFlags.Glow) || !sq.Has(SquareFlags.Lit)) return new MapShade(0.04f);
        var radius = Math.Max(1, _game.Player.LightRadius);
        var near = Math.Clamp(new Loc(x, y).DistanceTo(_game.Player.Position) / (radius + 0.75), 0, 1);
        return new MapShade((float)(0.08 + 0.5 * near * near), Torch: true, Warmth: (float)(0.16 * (1 - near)));
    }

    [ObservableProperty] private bool _showWholeMap;

    partial void OnShowWholeMapChanged(bool value) => Revision++;

    /// <summary>Quick start: the last character created, or a warrior on the very first run.</summary>
    public void StartGame(ulong seed)
    {
        if (LastCharacter() is { } spec && _data.Class(spec.ClassId) is not null) StartGame(seed, spec);
        else StartGame(seed, _settings.LastClass);
    }

    public void StartGame(ulong seed, string classId)
    {
        if (_data.Class(classId) is null) classId = _data.Classes.FirstOrDefault()?.Id ?? classId;
        StartGame(seed, _data.Class(classId) is null ? null! : CharacterSpec.Default(_data.Race("human")?.Id ?? "human", classId));
    }

    /// <summary>Starts a game with a character from the creation screen, remembering it for quick starts.</summary>
    public void StartCharacter(CharacterSpec spec)
    {
        _settings.LastCharacter = new SavedCharacter
        {
            Name = spec.Name, Race = spec.RaceId, Class = spec.ClassId,
            Stats = new Dictionary<string, int>(spec.BaseStats), Rolled = spec.Method == StatMethod.Roll,
            BirthOptions = spec.Options is { } birth ? new Dictionary<string, bool>(birth) : [],
        };
        _settings.LastClass = spec.ClassId;
        _saveSettings?.Invoke(_settings);
        StartGame((ulong)Environment.TickCount64, spec);
    }

    public CharacterSpec? LastCharacter() => _settings.LastCharacter is { } c
        ? new CharacterSpec(c.Name, c.Race, c.Class, c.Stats, c.Rolled ? StatMethod.Roll : StatMethod.PointBuy,
            c.BirthOptions.Count > 0 ? c.BirthOptions : null)
        : null;

    /// <summary>True until a character has been created (the app then opens the creation screen).</summary>
    public bool IsFirstRun => _settings.LastCharacter is null && !_loadedAtStartup;

    private bool _loadedAtStartup;

    /// <summary>Raised when the creation screen should open (the view shows it).</summary>
    public event Action? NewCharacterRequested;

    public void RequestNewCharacter() => NewCharacterRequested?.Invoke();

    public CharacterCreationViewModel CreateCharacterCreation()
    {
        var creation = new CharacterCreationViewModel(_data, LastCharacter());
        creation.Started += StartCharacter;
        return creation;
    }

    private void StartGame(ulong seed, CharacterSpec? spec)
    {
        // Starting afresh keeps the character being left behind (if still alive).
        TrySave();
        AttachGame(spec is null ? GameSession.NewGame(_data, seed) : GameSession.NewGame(_data, seed, spec));
        AddMessage($"Welcome, {_game.Player.Name} the {_game.Player.Race?.Name} {_game.Player.Class?.Name}! (seed {seed})"
                   + (_game.StudyableSpells().Any() ? " Press G to learn your first spell." : ""));
        Refresh();
    }

    /// <summary>Makes a new or loaded game the current one.</summary>
    private void AttachGame(GameSession game)
    {
        _game = game;
        _game.Lore = _lore;
        CursorMode = CursorMode.None;
        _game.Events.Subscribe<MessageEvent>(m => AddMessage(m.Text));
        _game.Events.Subscribe<ShopEnteredEvent>(OnShopEntered);
        _game.Events.Subscribe<LevelChangedEvent>(OnLevelChanged);
        _game.Events.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        _game.Events.Subscribe<RecalledEvent>(OnRecalled);
        _game.Events.Subscribe<UniqueFirstSeenEvent>(OnUniqueFirstSeen);
        _game.Events.Subscribe<ProjectionEvent>(OnProjection);
        _game.Events.Subscribe<MonsterDamagedEvent>(OnMonsterDamaged);
        Effects.Clear();
        IsInStore = false;
        IsPrompting = false;
        AttachAudio();
        ApplyOptions();
        Messages.Clear();
        _history.Clear();
        _lastCommand = null;
        StartRecording();
        CloseTitle(); // a game started or loaded from the title screen
        Refresh();
    }

    /// <summary>Runs a command from any input device.</summary>
    public void Execute(GameCommand command)
    {
        if (AskAboutRecall(command) || AskAboutLava(command) || TutorialStairs(command)) return;
        command = WithCount(command);
        Effects.Clear(); // a new command cuts short whatever the last one is still showing
        var fromDepth = _game.Player.Depth;
        NoteForRepeat(command, _game.Execute(command));
        ShowScenes(command, fromDepth);
        _game.AimAtTargetNext = false;
        CountDownHint();
        AutosaveIfDue();
        Refresh();
    }

    /// <summary>Ctrl+N: open the character creation screen.</summary>
    [RelayCommand]
    private void NewGame() => RequestNewCharacter();

    /// <summary>Start again straight away with the last character.</summary>
    [RelayCommand]
    private void QuickStart() => StartGame((ulong)Environment.TickCount64);

    /// <summary>Ctrl+F: the level feeling again.</summary>
    [RelayCommand]
    public void ShowFeeling()
    {
        if (!_game.ShowFeeling()) AddMessage("You have no feelings about this place (level feelings are off for this character).");
        Refresh();
    }

    [RelayCommand]
    private void RegenerateLevel() => Debug(() => Execute(new DebugJumpCommand(_game.Player.Depth)));

    [RelayCommand]
    private void CureAll() => Debug(() => Execute(new DebugCureAllCommand()));

    [RelayCommand]
    private void JumpDeeper() => Debug(() => Execute(new DebugJumpCommand(_game.Player.Depth + 5)));

    /// <summary>Debug: straight down to the next level, at a random open spot (no stairs under you).</summary>
    [RelayCommand]
    private void JumpNextLevel()
    {
        if (_game.Player.Depth >= _data.Constants.MaxDepth)
        {
            AddMessage("You are as deep as the dungeon goes.");
            Refresh();
            return;
        }
        Debug(() => Execute(new DebugJumpCommand(_game.Player.Depth + 1)));
    }

    /// <summary>Debug: back to town at once, where Word of Recall would put you — without its delay or message.</summary>
    [RelayCommand]
    private void JumpToTown()
    {
        if (_game.Player.Depth == 0)
        {
            AddMessage("You are already in town.");
            Refresh();
            return;
        }
        Debug(() => Execute(new DebugJumpCommand(0)));
    }

    /// <summary>Debug: whether shops pay gold for what you sell (birth_no_selling off), switchable mid-game.</summary>
    public bool ShopsPayGold => !_game.NoSelling;

    [RelayCommand]
    private void ToggleShopsPayGold() => Debug(() =>
    {
        _game.DebugSetShopsPay(!ShopsPayGold);
        OnPropertyChanged(nameof(ShopsPayGold));
        OnPropertyChanged(nameof(StoreModeText));
        RefreshStore();
        Refresh();
    });

    /// <summary>
    /// Runs a debug command. The first one a character uses asks first, as Angband's debug mode does:
    /// afterwards the character is marked (<see cref="GameSession.UsedDebug"/>) and never scored.
    /// </summary>
    private void Debug(Action action)
    {
        if (_game.UsedDebug)
        {
            action();
            return;
        }
        AskFirst("Debug commands mark this character: it won't enter the high scores. Use them anyway?", () =>
        {
            _game.MarkDebugUsed();
            action();
        });
        Refresh();
    }

    [RelayCommand]
    private void ToggleWholeMap()
    {
        if (ShowWholeMap) ShowWholeMap = false; // hiding it again is never a question
        else Debug(() =>
        {
            _game.MarkDebugUsed();
            ShowWholeMap = true;
        });
    }


    internal void Refresh()
    {
        OnPropertyChanged(nameof(ShopsPayGold)); // a new or loaded game may differ
        var level = _game.Level;
        var player = _game.Player;
        var depth = player.Depth;
        var where = depth == 0 ? "Town" : $"{depth * _data.Constants.FeetPerLevel} ft (L{depth})";
        var hunger = _game.HungerLevel;
        var effects = string.Join(" ", player.Timed.Active
            .Select(kv => _data.Timed(kv.Key)?.GradeAt(kv.Value)?.Label ?? kv.Key)
            .Prepend(hunger == HungerLevel.Fed ? "" : hunger.ToString())
            .Append(player.RecallTimer > 0 ? "Recall" : "")
            .Append(player.DeepDescentTimer > 0 ? "Descent" : "")
            .Append(SpeedText())
            .Append(_game.InArena ? "Single combat" : "")
            .Append(_game.Commanded is { IsActive: true } commanded ? $"Commanding {_game.MonsterName(commanded)}" : "")
            .Append(player.IsWinner ? "*WINNER*" : "")
            .Append(_game.PlayerShape is { } form ? $"Form: {form.Name}" : "")
            .Append(_game.TargetOkay() ? "Target: " + (_game.TargetMonster is { } tm ? _game.MonsterName(tm) : "a spot") : "")
            .Where(s => s.Length > 0));
        var quiver = player.Inventory.Quiver.Where(q => q.IsAmmo).Sum(q => q.Number);
        var thrown = player.Inventory.Quiver.Where(q => !q.IsAmmo).Sum(q => q.Number);
        var ammo = (quiver > 0 ? $"{quiver} missiles" : "no ammo") + (thrown > 0 ? $" + {thrown} to throw" : "");
        RefreshInventory();
        if (IsInStore) RefreshStore();
        RefreshListPanels();
        RefreshHotbar();
        RefreshPaperDolls();
        CheckHints();
        var next = player.Level >= Angband.Core.Magic.StatTables.MaxLevel ? "max" : _game.ExperienceForLevel(player.Level).ToString(CultureInfo.InvariantCulture);
        var mana = player.MaxMana > 0 ? $"  SP {player.Mana}/{player.MaxMana}" : "";
        StatusText = player.IsDead && player.IsWinner
            ? $"*** RETIRED VICTORIOUS ***  |  Level {player.Level}  |  Exp {player.Experience}  |  Ctrl+N for a new game"
            : player.IsDead
            ? $"*** DEAD *** killed by {player.KilledBy} on {where}  |  Level {player.Level}  |  Exp {player.Experience}  |  Ctrl+N for a new game"
            : string.Format(CultureInfo.InvariantCulture,
                "{14} the {15} {10} L{11}  |  HP {0}/{1}{12}  |  Exp {2}/{13}  |  {3}{4}  |  {5}  |  Turn {6}  |  {7}  |  Seed {8}{9}",
                player.Hp, player.MaxHp, player.Experience,
                where, depth == 0 ? (_game.IsDaytime ? " (day)" : " (night)") : _game.FeelingStatus is { } lf ? " " + lf : "",
                level.ProfileId, _game.NormalTurns, ammo, _game.Seed,
                effects.Length > 0 ? "  |  " + effects : "",
                player.Class?.Name ?? "", player.Level, mana, next, player.Name, player.Race?.Name ?? "");
        UpdateHealthBar();
        UpdateHover();
        UpdatePath();
        RefreshMapSummary();
        OnPropertyChanged(nameof(Map));
        Revision++;
    }

    // The message line's last message and how many times in a row it has been said.
    private string? _lineText;
    private int _lineCount;

    private void AddMessage(string text)
    {
        // A message said again straight away is counted on the line, as the history does ("<x5>"),
        // unless something else has been shown there since.
        var repeat = _lineText == text && LastMessage == LineDisplay(_lineText, _lineCount);
        _lineCount = repeat ? _lineCount + 1 : 1;
        _lineText = text;
        LastMessage = LineDisplay(text, _lineCount);
        Messages.Insert(0, text);
        while (Messages.Count > MaxMessages) Messages.RemoveAt(Messages.Count - 1);

        // The longer log for the message history: a repeat of the last message is counted instead.
        if (_history.Count > 0 && _history[^1].Text == text) _history[^1] = _history[^1] with { Count = _history[^1].Count + 1 };
        else _history.Add(new LoggedMessage(text, 1, _game?.NormalTurns ?? 0));
        if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
    }

    private static string LineDisplay(string text, int count) => count > 1 ? $"{text} <x{count}>" : text;

    /// <summary>Angband keeps 2048 messages.</summary>
    public const int MaxHistory = 2048;
    private readonly List<LoggedMessage> _history = [];

    /// <summary>The message history, oldest first.</summary>
    public IReadOnlyList<LoggedMessage> History => _history;
}
