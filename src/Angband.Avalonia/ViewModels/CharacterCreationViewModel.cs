using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Magic;
using Angband.Core.Randomness;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>One stat line: base value, race and class modifiers, and the result.</summary>
public sealed partial class StatRow(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BaseText))] private int _base = 10;

    /// <summary>The base stat as Angband writes it (18/50 for a heroic 23).</summary>
    public string BaseText => StatTables.Format(Base);
    [ObservableProperty] private string _raceMod = "";
    [ObservableProperty] private string _classMod = "";
    [ObservableProperty] private string _final = "";

    /// <summary>The autoroller's minimum for the final stat: an index into <see cref="CharacterCreationViewModel.MinimumChoices"/> (0: any).</summary>
    [ObservableProperty] private int _minimumIndex;

    /// <summary>The minimum as a stat value (0: none).</summary>
    public int Minimum => MinimumIndex <= 0 ? 0 : CharacterCreationViewModel.LowestMinimum - 1 + MinimumIndex;
}

/// <summary>
/// One race in the creation screen's comparison: its stat changes, hit die, experience and what it
/// can do (AVABand's abilities by name, when the birth option keeps them; Angband's own after them).
/// </summary>
public sealed record RaceComparisonRow(RaceDef Race, string Name, string Str, string Int, string Wis, string Dex, string Con,
    string HitDie, string Experience, string Abilities, bool IsSelected)
{
    /// <summary>The row in words (its accessible name).</summary>
    public string Spoken => $"{Name}: Strength {Say(Str)}, Intelligence {Say(Int)}, Wisdom {Say(Wis)}, Dexterity {Say(Dex)}, "
                            + $"Constitution {Say(Con)}, hit die {HitDie}, experience {Experience}. {Abilities}";

    private static string Say(string mod) => mod is "" or "0" ? "0" : mod;
}

/// <summary>A challenge run on the creation screen: ticking it sets its birth options, untick puts them back to their defaults.</summary>
public sealed partial class ChallengeRow : ObservableObject
{
    private readonly Action<ChallengeRow, bool> _changed;
    private bool _showing;

    public ChallengeRow(ChallengeDef def, Action<ChallengeRow, bool> changed)
    {
        Def = def;
        _changed = changed;
    }

    public ChallengeDef Def { get; }
    public string Name => Def.Name;
    public string Description => Def.Description;

    [ObservableProperty] private bool _isChecked;

    /// <summary>Shows whether the birth options make this challenge (without setting them again).</summary>
    internal void Show(bool on)
    {
        _showing = true;
        IsChecked = on;
        _showing = false;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_showing) _changed(this, value);
    }
}

/// <summary>A starting kit the creation screen offers: the class's own (no id), or one unlocked through feats.</summary>
public sealed record KitChoice(string? Id, string Label, string Description)
{
    public override string ToString() => Label;
}

/// <summary>One way of choosing stats, as the creation screen lists it.</summary>
public sealed record StatMethodChoice(StatMethod Method, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The character creation screen (Angband's birth menu): name, race, class and stats by point-buy
/// or rolling, with a live preview computed by actually creating the character in the engine.
/// </summary>
public sealed partial class CharacterCreationViewModel : ObservableObject
{
    private static readonly (string Id, string Label)[] StatLabels =
        [("str", "Strength"), ("int", "Intelligence"), ("wis", "Wisdom"), ("dex", "Dexterity"), ("con", "Constitution")];

    /// <summary>The lowest minimum the autoroller offers (below it, every roll qualifies).</summary>
    public const int LowestMinimum = 8;

    /// <summary>The autoroller's minimums: "any", then 8 up to 18/220.</summary>
    public static IReadOnlyList<string> MinimumChoices { get; } =
        ["any", .. Enumerable.Range(LowestMinimum, 40 - LowestMinimum + 1).Select(StatTables.Format)];

    /// <summary>The ways of choosing stats: Angband 4.2's two, and AVABand's heroic two.</summary>
    public static IReadOnlyList<StatMethodChoice> StatMethods { get; } =
    [
        new(StatMethod.PointBuy, "Point-buy"),
        new(StatMethod.Roll, "Rolled"),
        new(StatMethod.HeroicRoll, "Heroic roll"),
        new(StatMethod.HeroicPointBuy, "Heroic point-buy"),
    ];

    private readonly GameData _data;
    private readonly Random _random = new();

    public CharacterCreationViewModel(GameData data, CharacterSpec? last, IReadOnlyDictionary<string, int>? minimums = null,
        IReadOnlyList<StartingKitDef>? kits = null)
    {
        _data = data;
        // The class's own kit, then any others unlocked through feats (AVABand's own).
        Kits = [new KitChoice(null, "Standard kit", "The class's own kit, as Angband gives it."),
                .. (kits ?? []).Select(k => new KitChoice(k.Id, k.Name, k.Description + " The gold left over is the same or a little more."))];
        _selectedKit = Kits.FirstOrDefault(k => k.Id == last?.Kit) ?? Kits[0];
        Races = data.Races;
        Classes = [.. data.Classes, .. data.Multiclasses]; // (Angband's nine, then AVABand's Warrior/Mage and the rest)
        foreach (var (id, label) in StatLabels)
        {
            var row = new StatRow(id, label);
            if (minimums?.GetValueOrDefault(id) is >= LowestMinimum and var min) row.MinimumIndex = min - LowestMinimum + 1;
            StatRows.Add(row);
        }

        _name = last?.Name ?? "Adventurer";
        _selectedRace = Races.FirstOrDefault(r => r.Id == last?.RaceId) ?? Races.FirstOrDefault();
        _selectedClass = Classes.FirstOrDefault(c => c.Id == last?.ClassId) ?? Classes.FirstOrDefault();
        _selectedMethod = StatMethods.First(m => m.Method == (last?.Method ?? StatMethod.PointBuy));
        // A fresh character starts at 12 in everything, leaving half the points to spend.
        foreach (var row in StatRows) row.Base = last?.BaseStats.GetValueOrDefault(row.Id) is > 0 and var v ? v : 12;
        if (IsPointBuy && HeroicBirth.ValidatePointBuy(Method, BaseStats(), _data.Constants) is not null)
            foreach (var row in StatRows) row.Base = 12;
        // Birth options (Angband's birth menu), as last chosen or at their defaults.
        foreach (var o in OptionCatalog.OfKind(OptionKind.Birth))
            BirthOptionRows.Add(new OptionRow(o, last?.Options?.TryGetValue(o.Id, out var chosen) == true ? chosen : o.Default,
                true, (_, _) => { if (!_settingOptions) Update(); }));
        foreach (var c in Angband.Core.Game.Challenges.All) ChallengeRows.Add(new ChallengeRow(c, ChallengeChanged));
        Update();
    }

    public IReadOnlyList<RaceDef> Races { get; }
    public IReadOnlyList<ClassDef> Classes { get; }

    /// <summary>The starting kits on offer: the standard one, and those unlocked through feats.</summary>
    public IReadOnlyList<KitChoice> Kits { get; }

    /// <summary>Whether there is a kit to choose (some unlocked).</summary>
    public bool HasKitChoice => Kits.Count > 1;

    [ObservableProperty] private KitChoice _selectedKit;

    partial void OnSelectedKitChanged(KitChoice value) => Update();
    public ObservableCollection<StatRow> StatRows { get; } = [];

    /// <summary>Every race side by side (Compare races), the chosen one marked.</summary>
    public ObservableCollection<RaceComparisonRow> RaceComparison { get; } = [];
    public ObservableCollection<OptionRow> BirthOptionRows { get; } = [];

    /// <summary>The challenge runs, ticked when the birth options make them.</summary>
    public ObservableCollection<ChallengeRow> ChallengeRows { get; } = [];

    /// <summary>What the chosen difficulty means ("Custom" when the birth options match no preset).</summary>
    [ObservableProperty] private string _difficultyText = "";

    /// <summary>The ticked challenges explained, or what challenges are.</summary>
    [ObservableProperty] private string _challengeText = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRelaxed), nameof(IsClassic), nameof(IsHard))]
    private string? _presetId;

    public bool IsRelaxed => PresetId == "relaxed";
    public bool IsClassic => PresetId == "classic";
    public bool IsHard => PresetId == "hard";

    // (Set while a preset or challenge sets several birth options, so the preview updates once.)
    private bool _settingOptions;

    /// <summary>A birth option's value as ticked here.</summary>
    private bool BirthValue(string id) =>
        BirthOptionRows.FirstOrDefault(r => r.Id == id)?.IsChecked ?? OptionCatalog.Find(id)?.Default ?? false;

    /// <summary>Sets the birth options a difficulty preset names (the rest stay as they are).</summary>
    [RelayCommand]
    private void ApplyPreset(string id)
    {
        if (Angband.Core.Game.Challenges.Presets.FirstOrDefault(p => p.Id == id) is { } preset) SetOptions(preset.Options);
    }

    private void ChallengeChanged(ChallengeRow row, bool on) =>
        SetOptions(on ? row.Def.Options : row.Def.Options.ToDictionary(o => o.Key, o => OptionCatalog.Find(o.Key)?.Default ?? false));

    private void SetOptions(IReadOnlyDictionary<string, bool> values)
    {
        _settingOptions = true;
        try
        {
            foreach (var row in BirthOptionRows)
                if (values.TryGetValue(row.Id, out var value)) row.IsChecked = value;
        }
        finally
        {
            _settingOptions = false;
        }
        Update();
    }

    /// <summary>The presets and challenges as the birth options now stand.</summary>
    private void UpdateDifficulty()
    {
        var preset = Angband.Core.Game.Challenges.PresetFor(BirthValue);
        PresetId = preset?.Id;
        DifficultyText = preset is null ? "Custom: your own mix of the birth options below." : $"{preset.Name}: {preset.Description}";
        foreach (var row in ChallengeRows) row.Show(row.Def.IsOn(BirthValue));
        var on = ChallengeRows.Where(r => r.IsChecked && !(r.Def == Angband.Core.Game.Challenges.ForcedDescent
                                                          && Angband.Core.Game.Challenges.Ironman.IsOn(BirthValue))).ToList();
        ChallengeText = on.Count == 0
            ? "Optional ways to make it harder, each with its own feats and a tag on the high scores and your headstone."
            : string.Join(" ", on.Select(r => $"{r.Name}: {r.Description}"));
    }
    public int Budget => HeroicBirth.Budget(Method, _data.Constants);

    public StatMethod Method => SelectedMethod.Method;

    /// <summary>Point-buy of either kind (the +/− buttons show); otherwise rolled (Reroll and the autoroller show).</summary>
    public bool IsPointBuy
    {
        get => Method.IsPointBuy();
        set
        {
            if (value != IsPointBuy) SelectedMethod = StatMethods.First(m => m.Method == (value ? StatMethod.PointBuy : StatMethod.Roll));
        }
    }

    public bool IsRolled => !IsPointBuy;

    [ObservableProperty] private string _name;
    [ObservableProperty] private RaceDef? _selectedRace;
    [ObservableProperty] private ClassDef? _selectedClass;
    [ObservableProperty] private StatMethodChoice _selectedMethod;
    [ObservableProperty] private string _autorollText = "";
    [ObservableProperty] private string _pointsText = "";
    [ObservableProperty] private string _preview = "";

    /// <summary>The portrait of the race chosen in the class chosen (art/portraits).</summary>
    [ObservableProperty] private global::Avalonia.Media.Imaging.Bitmap? _portrait;

    /// <summary>"Adventurer the Dwarf Warrior/Priest", beside the portrait.</summary>
    [ObservableProperty] private string _portraitCaption = "";
    [ObservableProperty] private string _raceDescription = "";
    [ObservableProperty] private string _classDescription = "";
    [ObservableProperty] private string? _error;

    /// <summary>Raised with the finished character when the player presses Start.</summary>
    public event Action<CharacterSpec>? Started;

    partial void OnSelectedRaceChanged(RaceDef? value) => Update();

    /// <summary>Choosing a race from the comparison.</summary>
    [RelayCommand]
    private void ChooseRace(RaceDef race) => SelectedRace = race;
    partial void OnSelectedClassChanged(ClassDef? value) => Update();
    partial void OnNameChanged(string value) => Update();

    partial void OnSelectedMethodChanged(StatMethodChoice value)
    {
        OnPropertyChanged(nameof(IsPointBuy));
        OnPropertyChanged(nameof(IsRolled));
        OnPropertyChanged(nameof(Method));
        OnPropertyChanged(nameof(Budget));
        AutorollText = "";
        if (IsPointBuy)
        {
            var (min, max) = HeroicBirth.BaseRange(Method);
            foreach (var row in StatRows) row.Base = Math.Clamp(row.Base, min, max);
            while (HeroicBirth.PointsSpent(Method, BaseStats()) > Budget)
                StatRows.OrderByDescending(r => r.Base).First().Base--;
            Update();
        }
        else Reroll();
    }

    [RelayCommand]
    private void Increase(StatRow row)
    {
        if (!IsPointBuy || row.Base >= HeroicBirth.BaseRange(Method).Max) return;
        row.Base++;
        if (HeroicBirth.PointsSpent(Method, BaseStats()) > Budget) row.Base--;
        Update();
    }

    [RelayCommand]
    private void Decrease(StatRow row)
    {
        if (!IsPointBuy || row.Base <= Birth.PointBuyMin) return;
        row.Base--;
        Update();
    }

    [RelayCommand]
    private void Reroll()
    {
        var rolled = HeroicBirth.Roll(Method, new GameRandom((ulong)_random.NextInt64()));
        foreach (var row in StatRows) row.Base = rolled[row.Id];
        AutorollText = "";
        Update();
    }

    /// <summary>The autoroller's minimums, by stat (only those set).</summary>
    public Dictionary<string, int> Minimums() =>
        StatRows.Where(r => r.Minimum > 0).ToDictionary(r => r.Id, r => r.Minimum);

    /// <summary>
    /// The autoroller (as Angband 3.x's): rerolls, up to 100,000 times, until every final stat
    /// reaches its minimum — or says which minimum this roll can never reach.
    /// </summary>
    [RelayCommand]
    private void Autoroll()
    {
        if (IsPointBuy) return;
        var minimums = Minimums();
        if (minimums.Count == 0)
        {
            AutorollText = "Set a minimum for some stats first (the Min column), then Autoroll.";
            return;
        }
        if (HeroicBirth.Unreachable(Method, minimums, SelectedRace, SelectedClass) is { } impossible)
        {
            AutorollText = impossible;
            return;
        }
        const int tries = 100_000;
        if (HeroicBirth.AutoRoll(Method, minimums, SelectedRace, SelectedClass, new GameRandom((ulong)_random.NextInt64()), tries) is not { } found)
        {
            AutorollText = $"No roll met every minimum in {tries:N0} tries; lower some of them.";
            return;
        }
        foreach (var row in StatRows) row.Base = found.Stats[row.Id];
        Update();
        AutorollText = found.Rolls == 1 ? "The first roll met every minimum." : $"Met every minimum after {found.Rolls:N0} rolls.";
    }

    /// <summary>Angband player_random_name: a made-up word of 4 to 8 letters, built from names.txt's Tolkien names.</summary>
    [RelayCommand]
    private void RandomName()
    {
        var name = Angband.Core.Items.RandomName.Make(new GameRandom((ulong)_random.NextInt64()), _data.NameWords, 4, 8);
        Name = char.ToUpperInvariant(name[0]) + name[1..];
    }

    [RelayCommand]
    private void Start()
    {
        if (Spec() is not { } spec || Error is not null) return;
        Started?.Invoke(spec);
    }

    private Dictionary<string, int> BaseStats() => StatRows.ToDictionary(r => r.Id, r => r.Base);

    public CharacterSpec? Spec() =>
        SelectedRace is null || SelectedClass is null
            ? null
            : new CharacterSpec(string.IsNullOrWhiteSpace(Name) ? "Adventurer" : Name.Trim(), SelectedRace.Id, SelectedClass.Id,
                BaseStats(), Method,
                BirthOptionRows.ToDictionary(r => r.Id, r => r.IsChecked)) { Kit = SelectedKit?.Id };

    /// <summary>Refreshes modifiers, points and the preview (by creating the character for real).</summary>
    private void Update()
    {
        var race = SelectedRace;
        var cls = SelectedClass;
        foreach (var row in StatRows)
        {
            row.RaceMod = Mod(race?.Stats.GetValueOrDefault(row.Id) ?? 0);
            row.ClassMod = Mod(cls?.Stats.GetValueOrDefault(row.Id) ?? 0);
            row.Final = StatTables.Format(Birth.FinalStat(row.Base, race, cls, row.Id));
        }

        var spent = HeroicBirth.PointsSpent(Method, BaseStats());
        var heroic = Method.IsHeroic() ? $"  ·  Heroic (AVABand): the score counts {Angband.Core.Records.Scoring.HeroicPercent}%, tagged in the high scores" : "";
        PointsText = (IsPointBuy
            ? $"Points: {Budget - spent} of {Budget} left (unspent points become {50} gold each)"
            : Method == StatMethod.HeroicRoll
                ? "Heroic roll: each stat 14 to 18/50 (Reroll, or set minimums and Autoroll)"
                : "Rolled stats (Reroll, or set minimums and Autoroll)") + heroic;
        Error = IsPointBuy ? HeroicBirth.ValidatePointBuy(Method, BaseStats(), _data.Constants) : null;
        UpdateDifficulty();
        UpdateRaceComparison();
        Portrait = Angband.Avalonia.Controls.Portraits.For(race?.Id, cls?.Id);
        PortraitCaption = $"{(string.IsNullOrWhiteSpace(Name) ? "Adventurer" : Name.Trim())} the {race?.Name} {cls?.Name}";
        RaceDescription = (race?.Description ?? "")
                          + (race is { AvaAbilityText.Length: > 0 } ? $"\n\nAVABand: {race.AvaAbilityText}" : "");
        ClassDescription = cls?.Description ?? "";

        if (Spec() is not { } spec)
        {
            Preview = "";
            return;
        }
        var p = GameSession.NewGame(_data, 0, spec).Player;
        // Angband's birth screen names them from player_property.txt (race_help, class_help).
        var abilities = new List<string>();
        if (p.Infravision > 0) abilities.Add($"infravision {p.Infravision * 10} ft");
        abilities.AddRange(Birth.Abilities(_data, race, cls).Select(a => a.Name));
        var realm = cls?.Realm is { } r ? _data.Realm(r) : null;

        Preview = string.Join(Environment.NewLine, new[]
        {
            $"{spec.Name} the {race?.Name} {cls?.Name}",
            $"Hit points {p.MaxHp}   Hit die d{p.HitDie}   Experience {p.ExpFactor}%",
            realm is null ? "No magic" : $"{realm.Name} magic ({realm.Stat.ToUpperInvariant()}): {p.MaxMana} spell points at level 1"
                                         + (cls!.FirstSpellLevel > 1 ? $" (spells from level {cls.FirstSpellLevel})" : ""),
            $"Armour {p.Armour}   To-hit {p.ToHit:+0;-0}   To-dam {p.ToDam:+0;-0}   Carry {p.WeightLimit / 20} lb unhindered",
            $"Melee {p.SkillMelee}   Shooting {p.SkillBow}   Throwing {p.SkillThrow}   Saving throw {p.SkillSave}   Stealth {p.Stealth}   Disarm {p.DisarmSkill} (magic {p.DisarmMagicSkill})   Searching {p.SkillSearch}",
            abilities.Count > 0 ? "Abilities: " + string.Join(", ", abilities) : "No special abilities",
            $"Starting gold {p.Gold}",
        });
    }

    /// <summary>A flag with no wording of its own, readably: "SOME_FLAG" → "some flag" (never the raw id).</summary>

    /// <summary>Rebuilds the race comparison (the chosen race, and whether AVABand's abilities are on, change it).</summary>
    private void UpdateRaceComparison()
    {
        var avaRaces = BirthOptionRows.FirstOrDefault(r => r.Id == OptionIds.AvaRaces)?.IsChecked ?? true;
        string Stat(RaceDef r, string id) => Mod(r.Stats.GetValueOrDefault(id)) is { Length: > 0 } m ? m : "0";
        RaceComparison.Clear();
        foreach (var r in Races)
        {
            var abilities = new List<string>();
            if (avaRaces) abilities.AddRange(AbilityNames(r.AvaAbilityText));
            if (avaRaces && r.AvaCarryPercent != 0) abilities.Add($"carries {Math.Abs(r.AvaCarryPercent)}% {(r.AvaCarryPercent > 0 ? "more" : "less")}");
            if (r.Infravision > 0) abilities.Add($"infravision {r.Infravision * 10} ft");
            abilities.AddRange(Birth.Abilities(_data, r, null).Select(a => a.Name));
            RaceComparison.Add(new RaceComparisonRow(r, r.Name, Stat(r, "str"), Stat(r, "int"), Stat(r, "wis"), Stat(r, "dex"), Stat(r, "con"),
                $"d{r.HitDie}", $"{r.ExpFactor}%", abilities.Count > 0 ? string.Join(", ", abilities) : "nothing special",
                ReferenceEquals(r, SelectedRace)));
        }
    }

    /// <summary>
    /// An ability text's names, each sentence's words before its colon: "Stone-thrower: +20 ... Second
    /// breakfast: ... Small: carries ..." → "Stone-thrower", "second breakfast" (what it carries is said as
    /// "carries 10% less", so those go).
    /// </summary>
    internal static IEnumerable<string> AbilityNames(string text)
    {
        var names = text.Split(". ", StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Contains(':') && !s[(s.IndexOf(':') + 1)..].TrimStart().StartsWith("carries", StringComparison.Ordinal))
            .Select(s => s[..s.IndexOf(':')].Trim())
            .ToList();
        return names.Select((n, i) => i == 0 ? n : char.ToLowerInvariant(n[0]) + n[1..]);
    }

    private static string Mod(int v) => v == 0 ? "" : v.ToString("+0;-0", CultureInfo.InvariantCulture);
}
