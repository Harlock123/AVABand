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
    private bool _warned;

    public SoundDirector(IAudioEngine engine) => _engine = engine;

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
        _warned = false;
        _subscriptions.Add(_bus.SubscribeAll(OnEvent));
        UpdateMusic();
    }

    public void Detach()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
        _bus = null;
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
                Play(s.SpellId, s.SpellId.StartsWith("S_") ? "SUM_MONSTER" : s.SpellId is "SCARE" ? "CAST_FEAR" : "");
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
            case LockPickedEvent:
                Play("LOCKPICK");
                break;
            case StairsTakenEvent st:
                Play(st.Down ? "STAIRS_DOWN" : "STAIRS_UP");
                break;
            case ItemPickedUpEvent p when p.KindId is "copper" or "silver" or "gold" or "mithril" or "adamantite":
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
                UpdateMusic();
                break;
            case DayNightChangedEvent d:
                _day = d.IsDaytime;
                UpdateMusic();
                break;
            case WorldTickEvent w:
                _depth = w.Depth;
                _day = w.IsDaytime;
                if (_random.Next(AmbientChance) == 0) Play(AmbientSound(w.Depth, w.IsDaytime));
                break;
        }
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

    /// <summary>Starts a track for the current mood (falling back town_night→town, deep→dungeon).</summary>
    private void UpdateMusic()
    {
        if (!MusicEnabled || MusicPack is not { } pack)
        {
            _engine.StopMusic();
            return;
        }
        var mood = MusicMood;
        var fallback = mood switch { "town_night" => "town", "deep" => "dungeon", _ => mood };
        if (!pack.Music.TryGetValue(mood, out var tracks) && !pack.Music.TryGetValue(fallback, out tracks))
        {
            _engine.StopMusic();
            return;
        }
        // Keep playing if the current track already belongs to this mood's playlist.
        if (_engine.CurrentMusic is { } current && tracks.Any(t => pack.Resolve(t) == current)) return;
        _engine.PlayMusic(pack.Resolve(tracks[_random.Next(tracks.Count)]));
    }

    public void Dispose() => Detach();
}
