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

    public CharacterCreationViewModel(GameData data, CharacterSpec? last, IReadOnlyDictionary<string, int>? minimums = null)
    {
        _data = data;
        Races = data.Races;
        Classes = data.Classes;
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
                true, (_, _) => Update()));
        Update();
    }

    public IReadOnlyList<RaceDef> Races { get; }
    public IReadOnlyList<ClassDef> Classes { get; }
    public ObservableCollection<StatRow> StatRows { get; } = [];
    public ObservableCollection<OptionRow> BirthOptionRows { get; } = [];
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
    [ObservableProperty] private string _raceDescription = "";
    [ObservableProperty] private string _classDescription = "";
    [ObservableProperty] private string? _error;

    /// <summary>Raised with the finished character when the player presses Start.</summary>
    public event Action<CharacterSpec>? Started;

    partial void OnSelectedRaceChanged(RaceDef? value) => Update();
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
                BirthOptionRows.ToDictionary(r => r.Id, r => r.IsChecked));

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
        RaceDescription = race?.Description ?? "";
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

    private static string Mod(int v) => v == 0 ? "" : v.ToString("+0;-0", CultureInfo.InvariantCulture);
}
