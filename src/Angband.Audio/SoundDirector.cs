using Angband.Core.Combat;
using Angband.Core.Game;

namespace Angband.Audio;

/// <summary>
/// Turns game events into sounds and picks the music. It only listens to the event bus, so the
/// engine knows nothing about audio. Sound choices use their own random generator so they never
/// disturb the game's deterministic RNG.
/// </summary>
public sealed class SoundDirector : IDisposable
{
    /// <summary>1-in-N chance per player turn of an ambient sound in the dungeon.</summary>
    public const int AmbientChance = 60;

    private readonly IAudioEngine _engine;
    private readonly Random _random = new();
    private readonly List<IDisposable> _subscriptions = [];
    private GameEventBus? _bus;
    private int _depth;
    private bool _day = true;
    private string _profile = "";
    private bool _warned;

    public SoundDirector(IAudioEngine engine)
    {
        _engine = engine;
        _engine.MusicEnded += OnMusicEnded;
    }

    // The music is chosen from the game's thread and, when a track ends, the audio thread.
    private readonly object _musicLock = new();
    private string? _lastTrack;

    public SoundPack? Effects { get; set; }

    public SoundPack? MusicPack
    {
        get => _musicPack;
        set
        {
            _musicPack = value;
            UpdateMusic();
        }
    }
    private SoundPack? _musicPack;

    public bool EffectsEnabled { get; set; } = true;

    public bool MusicEnabled
    {
        get => _musicEnabled;
        set
        {
            _musicEnabled = value;
            UpdateMusic();
        }
    }
    private bool _musicEnabled = true;

    /// <summary>Sound event names played (for tests and diagnostics).</summary>
    public event Action<string>? Played;

    /// <summary>Starts listening to a game's events (stops listening to any previous game).</summary>
    public void Attach(GameSession game)
    {
        Detach();
        _bus = game.Events;
        _depth = game.Player.Depth;
        _day = game.IsDaytime;
        _profile = game.Level.ProfileId;
        _warned = false;
        _subscriptions.Add(_bus.SubscribeAll(OnEvent));
        UpdateMusic();
        UpdateAmbience();
    }

    public void Detach()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
        _bus = null;
        _engine.StopAmbience();
    }

    /// <summary>The music mood for the current place: town / town_night / dungeon / deep.</summary>
    public string MusicMood => _depth == 0 ? (_day ? "town" : "town_night") : _depth >= 20 ? "deep" : "dungeon";

    private void OnEvent(IGameEvent e)
    {
        switch (e)
        {
            case PlayerAttackEvent a:
                Play(!a.Hit ? "MISS" : a.Ranged ? "SHOOT_HIT" : a.Critical switch
                {
                    CriticalGrade.Good => "HIT_GOOD",
                    CriticalGrade.Great => "HIT_GREAT",
                    CriticalGrade.Superb => "HIT_SUPERB",
                    CriticalGrade.HighGreat => "HIT_HI_GREAT",
                    CriticalGrade.HighSuperb => "HIT_HI_SUPERB",
                    _ => "HIT",
                });
                break;
            case MissileFiredEvent:
                Play("SHOOT");
                break;
            case MonsterAttackEvent { Hit: true } m:
                Play("MON_" + m.Method.ToUpperInvariant(), "MON_HIT");
                break;
            case MonsterKilledEvent k:
                Play(k.IsUnique ? "KILL_UNIQUE" : "KILL");
                break;
            case MonsterSpellEvent s:
                // The spell's own sound (4.2's msgt: a frost breath is BR_FROST, not BR_COLD).
                Play(s.Sound ?? s.SpellId, s.SpellId.StartsWith("S_") ? "SUM_MONSTER" : "");
                break;
            case MonsterBredEvent { Seen: true }:
                Play("MULTIPLY");
                break;
            case PlayerHurtEvent h:
                // Angband warns once when hit points fall below 30%.
                if (h.Hp * 10 < h.MaxHp * 3)
                {
                    if (!_warned) Play("HITPOINT_WARN");
                    _warned = true;
                }
                else _warned = false;
                break;
            case PlayerDiedEvent:
                Play("DEATH");
                break;
            case StatusChangedEvent st:
                Play(StatusSound(st.EffectId, st.Value > 0));
                break;
            case DoorOpenedEvent:
                Play("OPENDOOR");
                break;
            case DoorClosedEvent:
                Play("SHUTDOOR");
                break;
            case LockPickFailedEvent:
                Play("LOCKPICK_FAIL");
                break;
            case TrapFoundEvent:
                Play("NOTICE");
                break;
            case TrapDisarmedEvent:
                Play("DISARM");
                break;
            case LockPickedEvent:
                Play("LOCKPICK");
                break;
            case StairsTakenEvent st:
                Play(st.Down ? "STAIRS_DOWN" : "STAIRS_UP");
                break;
            case ItemPickedUpEvent { Gold: true } p:
                Play(p.Amount >= 200 ? "MONEY3" : p.Amount >= 50 ? "MONEY2" : "MONEY1");
                break;
            case ItemDroppedEvent:
                Play("DROP");
                break;
            case ItemWieldedEvent:
                Play("WIELD");
                break;
            case ItemUsedEvent u:
                Play(u.Verb switch { "quaff" => "QUAFF", "eat" => "EAT", "zap" or "aim" => "ZAP_ROD", "use" => "USE_STAFF", "activate" => "ACT_ARTIFACT", _ => "" });
                break;
            case RuneLearnedEvent:
                Play("RUNE");
                break;
            case HungerChangedEvent { Worse: true, Level: <= HungerLevel.Hungry }:
                Play("HUNGRY");
                break;
            case DigEvent { Done: false }:
                Play("DIG");
                break;
            case LevelUpEvent:
                Play("LEVEL");
                break;
            case SpellLearnedEvent:
                Play("STUDY");
                break;
            case SpellCastEvent c:
                Play(c.Realm == "divine" ? "PRAYER" : "SPELL");
                break;
            case ShopEnteredEvent shop:
                Play(shop.IsHome ? "STORE_HOME" : "STORE_ENTER");
                break;
            case ShopLeftEvent:
                Play("STORE_LEAVE");
                break;
            case ItemBoughtEvent or ItemSoldEvent:
                Play("STORE5");
                break;
            case PlayerTeleportedEvent:
                Play("TELEPORT");
                break;
            case LevelChangedEvent l:
                _depth = l.Depth;
                _profile = l.ProfileId;
                UpdateMusic();
                UpdateAmbience();
                break;
            case DayNightChangedEvent d:
                _day = d.IsDaytime;
                UpdateMusic();
                UpdateAmbience();
                break;
            case WorldTickEvent w:
                _depth = w.Depth;
                _day = w.IsDaytime;
                if (_random.Next(AmbientChance) == 0) Play(AmbientSound(w.Depth, w.IsDaytime));
                break;
        }
    }

    // --- Ambience: a loop under the music, for the place you are in ------------------------------

    /// <summary>Where ambience loops are looked for, in order (the player's own folder, then the game's).</summary>
    public IReadOnlyList<string> AmbienceFolders
    {
        get => _ambienceFolders;
        set
        {
            _ambienceFolders = value;
            UpdateAmbience();
        }
    }
    private IReadOnlyList<string> _ambienceFolders = [];

    public bool AmbienceEnabled
    {
        get => _ambienceEnabled;
        set
        {
            _ambienceEnabled = value;
            UpdateAmbience();
        }
    }
    private bool _ambienceEnabled = true;

    private static readonly string[] AmbienceExtensions = [".ogg", ".wav", ".mp3"];

    /// <summary>
    /// The loops for a place, best first: a cavern, labyrinth or fortress has its own; otherwise the
    /// town by day or night, or the dungeon by depth — shallow (to 1000 ft), deep (to 3000 ft) and
    /// the abyss below — each falling back to the one above it.
    /// </summary>
    public static IReadOnlyList<string> AmbienceNames(int depth, bool day, string profile)
    {
        var band = depth > 60 ? "ambient-dungeon-abyss" : depth > 20 ? "ambient-dungeon-deep" : "ambient-dungeon-shallow";
        var bands = band switch
        {
            "ambient-dungeon-abyss" => new[] { band, "ambient-dungeon-deep", "ambient-dungeon-shallow" },
            "ambient-dungeon-deep" => [band, "ambient-dungeon-shallow"],
            _ => [band],
        };
        if (depth == 0) return day ? ["ambient-town-day"] : ["ambient-town-night", "ambient-town-day"];
        return profile is "cavern" or "labyrinth" or "fortress" ? ["ambient-" + profile, .. bands] : bands;
    }

    /// <summary>The loop for where you are now, if there is one in the folders.</summary>
    public string? AmbienceFile()
    {
        foreach (var name in AmbienceNames(_depth, _day, _profile))
            foreach (var folder in _ambienceFolders)
                foreach (var ext in AmbienceExtensions)
                    if (Path.Combine(folder, name + ext) is var path && File.Exists(path)) return path;
        return null;
    }

    private void UpdateAmbience()
    {
        if (!_ambienceEnabled || _bus is null || AmbienceFile() is not { } file) _engine.StopAmbience();
        else _engine.PlayAmbience(file);
    }

    /// <summary>Angband AMBIENT_DAY/NITE in town, AMBIENT_DNG1..5 by depth band below.</summary>
    public static string AmbientSound(int depth, bool day) =>
        depth == 0 ? (day ? "AMBIENT_DAY" : "AMBIENT_NITE") : $"AMBIENT_DNG{Math.Clamp(1 + depth / 20, 1, 5)}";

    private static string StatusSound(string effect, bool starting) => (effect, starting) switch
    {
        ("blind", true) => "BLIND",
        ("confused", true) => "CONFUSED",
        ("poisoned", true) => "POISONED",
        ("afraid", true) => "AFRAID",
        ("paralyzed", true) => "PARALYZED",
        ("slow", true) => "SLOW",
        ("stun", true) => "STUN",
        ("cut", true) => "CUT",
        ("fast", true) => "SPEED",
        ("blessed", true) => "BLESSED",
        ("hero", true) => "HERO",
        ("oppose_fire", true) => "RES_FIRE",
        ("oppose_cold", true) => "RES_COLD",
        ("oppose_pois", true) => "RES_POIS",
        (_, false) when effect is "blind" or "confused" or "poisoned" or "afraid" or "paralyzed" or "slow" or "stun" or "cut" => "RECOVER",
        _ => "",
    };

    /// <summary>Plays the first event name the effects pack has a sound for.</summary>
    private void Play(params string[] names)
    {
        if (!EffectsEnabled || Effects is not { } pack) return;
        foreach (var name in names)
        {
            if (name.Length == 0 || !pack.Sounds.TryGetValue(name, out var files) || files.Count == 0) continue;
            _engine.PlayEffect(pack.Resolve(files[_random.Next(files.Count)]), pack.Volumes.GetValueOrDefault(name, 1f));
            Played?.Invoke(name);
            return;
        }
    }

    /// <summary>
    /// Starts a track for the current mood (falling back town_night→town, deep→dungeon). Tracks
    /// don't loop: when one ends, <see cref="OnMusicEnded"/> picks another from the playlist.
    /// </summary>
    private void UpdateMusic()
    {
        lock (_musicLock)
        {
            if (Playlist() is not { } tracks)
            {
                _engine.StopMusic();
                return;
            }
            // Keep playing if the current track already belongs to this mood's playlist.
            if (_engine.CurrentMusic is { } current && tracks.Contains(current)) return;
            PlayFrom(tracks);
        }
    }

    /// <summary>A track played out: on to another from the same playlist (not the same one, if there is a choice).</summary>
    private void OnMusicEnded()
    {
        lock (_musicLock)
        {
            if (Playlist() is { } tracks) PlayFrom(tracks);
        }
    }

    /// <summary>The current mood's tracks (as full paths), or null when there is no music to play.</summary>
    private List<string>? Playlist()
    {
        if (!MusicEnabled || MusicPack is not { } pack) return null;
        var mood = MusicMood;
        var fallback = mood switch { "town_night" => "town", "deep" => "dungeon", _ => mood };
        if (!pack.Music.TryGetValue(mood, out var tracks) && !pack.Music.TryGetValue(fallback, out tracks)) return null;
        return tracks.Count == 0 ? null : [.. tracks.Select(pack.Resolve)];
    }

    private void PlayFrom(List<string> tracks)
    {
        var choices = tracks.Count > 1 ? tracks.Where(t => t != _lastTrack).ToList() : tracks;
        var next = choices[_random.Next(choices.Count)];
        _lastTrack = next;
        _engine.PlayMusic(next, loop: false);
    }

    public void Dispose()
    {
        _engine.MusicEnded -= OnMusicEnded;
        Detach();
    }
}
