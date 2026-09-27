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
    [ObservableProperty] private int _base = 10;
    [ObservableProperty] private string _raceMod = "";
    [ObservableProperty] private string _classMod = "";
    [ObservableProperty] private string _final = "";
}

/// <summary>
/// The character creation screen (Angband's birth menu): name, race, class and stats by point-buy
/// or rolling, with a live preview computed by actually creating the character in the engine.
/// </summary>
public sealed partial class CharacterCreationViewModel : ObservableObject
{
    private static readonly (string Id, string Label)[] StatLabels =
        [("str", "Strength"), ("int", "Intelligence"), ("wis", "Wisdom"), ("dex", "Dexterity"), ("con", "Constitution")];

    private static readonly string[] NameSyllables =
        ["ar", "bor", "cel", "dor", "el", "fin", "gal", "hal", "ith", "lor", "mir", "nar", "or", "rin", "sil", "tha", "ul", "val", "wen", "dil"];

    private readonly GameData _data;
    private readonly Random _random = new();
    private Dictionary<string, int> _rolled = [];

    public CharacterCreationViewModel(GameData data, CharacterSpec? last)
    {
        _data = data;
        Races = data.Races;
        Classes = data.Classes;
        foreach (var (id, label) in StatLabels) StatRows.Add(new StatRow(id, label));

        _name = last?.Name ?? "Adventurer";
        _selectedRace = Races.FirstOrDefault(r => r.Id == last?.RaceId) ?? Races.FirstOrDefault();
        _selectedClass = Classes.FirstOrDefault(c => c.Id == last?.ClassId) ?? Classes.FirstOrDefault();
        _isPointBuy = last?.Method != StatMethod.Roll;
        // A fresh character starts at 12 in everything, leaving half the points to spend.
        foreach (var row in StatRows) row.Base = last?.BaseStats.GetValueOrDefault(row.Id) is > 0 and var v ? v : 12;
        if (!_isPointBuy) _rolled = StatRows.ToDictionary(r => r.Id, r => r.Base);
        if (_isPointBuy && Birth.ValidatePointBuy(BaseStats(), Budget) is not null)
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
    public int Budget => _data.Constants.BirthPoints;

    [ObservableProperty] private string _name;
    [ObservableProperty] private RaceDef? _selectedRace;
    [ObservableProperty] private ClassDef? _selectedClass;
    [ObservableProperty] private bool _isPointBuy;
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

    partial void OnIsPointBuyChanged(bool value)
    {
        if (value)
            foreach (var row in StatRows) row.Base = Math.Clamp(row.Base, Birth.PointBuyMin, Birth.PointBuyMax);
        else Reroll();
        while (value && Birth.PointsSpent(BaseStats()) > Budget)
            StatRows.OrderByDescending(r => r.Base).First().Base--;
        Update();
    }

    [RelayCommand]
    private void Increase(StatRow row)
    {
        if (!IsPointBuy || row.Base >= Birth.PointBuyMax) return;
        row.Base++;
        if (Birth.PointsSpent(BaseStats()) > Budget) row.Base--;
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
        _rolled = Birth.RollStats(new GameRandom((ulong)_random.NextInt64()));
        foreach (var row in StatRows) row.Base = _rolled[row.Id];
        Update();
    }

    [RelayCommand]
    private void RandomName()
    {
        var name = "";
        for (var i = _random.Next(2, 4); i > 0; i--) name += NameSyllables[_random.Next(NameSyllables.Length)];
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
                BaseStats(), IsPointBuy ? StatMethod.PointBuy : StatMethod.Roll,
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

        var spent = Birth.PointsSpent(BaseStats());
        PointsText = IsPointBuy
            ? $"Points: {Budget - spent} of {Budget} left (unspent points become {50} gold each)"
            : "Rolled stats (press Reroll for another set)";
        Error = IsPointBuy ? Birth.ValidatePointBuy(BaseStats(), Budget) : null;
        RaceDescription = race?.Description ?? "";
        ClassDescription = cls?.Description ?? "";

        if (Spec() is not { } spec)
        {
            Preview = "";
            return;
        }
        var p = GameSession.NewGame(_data, 0, spec).Player;
        var abilities = new List<string>();
        if (p.Infravision > 0) abilities.Add($"infravision {p.Infravision * 10} ft");
        abilities.AddRange(p.IntrinsicResists.Keys.Select(r => r switch
        {
            "free_act" => "free action", "blind" => "cannot be blinded", "pois" => "resists poison",
            "light" => "resists light", "dark" => "resists darkness", _ => r,
        }));
        if (p.Regenerates) abilities.Add("regenerates quickly");
        abilities.AddRange((cls?.Flags ?? []).Select(f => f switch
        {
            ClassFlags.Steal => "steals from monsters",
            ClassFlags.Unlight => "sees without light (spells falter on lit ground)",
            ClassFlags.ShieldBash => "shield bashes",
            ClassFlags.BlessWeapon => "+2 with hafted or blessed weapons",
            ClassFlags.CombatRegen => "mana from fighting, not rest",
            ClassFlags.ImpairHp => "heals slowly",
            _ => f.ToLowerInvariant(),
        }));
        var realm = cls?.Realm is { } r ? _data.Realm(r) : null;

        Preview = string.Join(Environment.NewLine, new[]
        {
            $"{spec.Name} the {race?.Name} {cls?.Name}",
            $"Hit points {p.MaxHp}   Hit die d{p.HitDie}   Experience {p.ExpFactor}%",
            realm is null ? "No magic" : $"{realm.Name} magic ({realm.Stat.ToUpperInvariant()}): {p.MaxMana} spell points at level 1"
                                         + (cls!.FirstSpellLevel > 1 ? $" (spells from level {cls.FirstSpellLevel})" : ""),
            $"Armour {p.Armour}   To-hit {p.ToHit:+0;-0}   To-dam {p.ToDam:+0;-0}   Carry {p.WeightLimit / 20} lb unhindered",
            $"Melee {p.SkillMelee}   Shooting {p.SkillBow}   Throwing {p.SkillThrow}   Saving throw {p.SkillSave}   Stealth {p.Stealth}   Disarm {p.DisarmSkill}",
            abilities.Count > 0 ? "Abilities: " + string.Join(", ", abilities) : "No special abilities",
            $"Starting gold {p.Gold}",
        });
    }

    private static string Mod(int v) => v == 0 ? "" : v.ToString("+0;-0", CultureInfo.InvariantCulture);
}
