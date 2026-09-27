using System.Collections.ObjectModel;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

public enum SpellPromptKind { Cast, Study, Browse }

/// <summary>A line in the spell list: level, mana, failure chance and state.</summary>
public sealed record SpellRow(string Letter, string Name, int Level, int Mana, int Fail, string Note, string Description, SpellDef Spell)
{
    public string Stats => Level > 0 ? $"Lv {Level,2}  Mana {Mana,2}  Fail {Fail,2}%" : "";
}

public sealed record ClassChoice(string Id, string Name);

/// <summary>Spell lists (cast, study, browse), aiming and the "not enough mana" confirmation.</summary>
public sealed partial class MainWindowViewModel
{
    private SpellPromptKind _spellPromptKind;
    private string? _confirmSpellId;

    public ObservableCollection<SpellRow> SpellPromptRows { get; } = [];

    public IReadOnlyList<ClassChoice> ClassChoices => _data.Classes.Select(c => new ClassChoice(c.Id, c.Name)).ToList();

    /// <summary>Spell waiting for a direction key, if any.</summary>
    [ObservableProperty] private string? _pendingSpellDirection;
    [ObservableProperty] private bool _isConfirming;

    [RelayCommand]
    private void NewGameAs(string classId)
    {
        var race = _settings.LastCharacter?.Race ?? "human";
        var name = _settings.LastCharacter?.Name ?? "Adventurer";
        StartCharacter(CharacterSpec.Default(_data.Race(race) is null ? "human" : race, classId, name));
    }

    public void BeginSpellPrompt(SpellPromptKind kind)
    {
        var realm = _game.PlayerRealm;
        if (realm is null)
        {
            AddMessage("You cannot use magic.");
            return;
        }

        var spells = kind switch
        {
            SpellPromptKind.Cast => _game.ClassSpells.Where(s => _game.Player.LearnedSpells.Contains(s.Id) && _game.HasBookFor(s)),
            SpellPromptKind.Study => _game.StudyableSpells(),
            _ => _game.ClassSpells.Where(_game.HasBookFor),
        };
        var list = spells.ToList();
        if (list.Count == 0)
        {
            AddMessage(kind switch
            {
                SpellPromptKind.Cast => $"You don't know any {realm.SpellNoun}s you can {realm.Verb} (you need their book).",
                SpellPromptKind.Study => $"You cannot learn any new {realm.SpellNoun}s right now.",
                _ => "You have no books you can read.",
            });
            return;
        }

        _spellPromptKind = kind;
        PromptTitle = kind switch
        {
            SpellPromptKind.Cast => $"{Capitalize(realm.Verb)} which {realm.SpellNoun}?",
            SpellPromptKind.Study => $"Learn which {realm.SpellNoun}?",
            _ => $"Your {realm.Name} {realm.SpellNoun}s (Esc to close)",
        };
        PromptRows.Clear();
        SpellPromptRows.Clear();
        for (var i = 0; i < list.Count && i < 26; i++)
        {
            var spell = list[i];
            var info = _game.SpellInfo(spell)!;
            var note = _game.Player.LearnedSpells.Contains(spell.Id) ? (_game.Player.CastSpells.Contains(spell.Id) ? "" : "untried")
                : info.Level > _game.Player.Level ? "difficult" : "unknown";
            SpellPromptRows.Add(new SpellRow(((char)('a' + i)).ToString(), spell.Name, info.Level, info.Mana,
                _game.SpellFailChance(spell), note, spell.Description, spell));
        }
        IsPrompting = true;
    }

    /// <summary>Picks a spell from the open spell list.</summary>
    private void ChooseSpell(SpellRow row)
    {
        var spell = row.Spell;
        switch (_spellPromptKind)
        {
            case SpellPromptKind.Study:
                Execute(new StudyCommand(spell.Id));
                break;
            case SpellPromptKind.Browse:
                AddMessage($"{spell.Name}: {spell.Description}");
                break;
            default:
                var info = _game.SpellInfo(spell)!;
                if (info.Mana > _game.Player.Mana)
                {
                    _confirmSpellId = spell.Id;
                    IsConfirming = true;
                    LastMessage = "You do not have enough mana. Attempt it anyway? (y/n)";
                    return;
                }
                CastOrAim(spell.Id, allowOverexert: false);
                break;
        }
    }

    private bool _overexertAfterAim;

    private void CastOrAim(string spellId, bool allowOverexert)
    {
        if (_data.Spell(spellId) is { NeedsDirection: true })
        {
            PendingSpellDirection = spellId;
            _overexertAfterAim = allowOverexert;
            LastMessage = "Direction?";
            return;
        }
        Execute(new CastCommand(spellId, AllowOverexert: allowOverexert));
    }

    /// <summary>Answers the "attempt it anyway?" question.</summary>
    public void Confirm(bool yes)
    {
        IsConfirming = false;
        if (ConfirmAction(yes)) return;
        if (ConfirmResumeShape(yes)) return;
        if (_confirmRetire)
        {
            _confirmRetire = false;
            if (yes) Execute(new RetireCommand());
            else LastMessage = "Cancelled.";
            return;
        }
        var spell = _confirmSpellId;
        _confirmSpellId = null;
        if (yes && spell is not null) CastOrAim(spell, allowOverexert: true);
        else LastMessage = "Cancelled.";
    }

    /// <summary>Completes a spell that was waiting for a direction.</summary>
    public void CastInDirection(Direction? dir)
    {
        var spell = PendingSpellDirection;
        PendingSpellDirection = null;
        if (spell is null) return;
        if (dir is null)
        {
            LastMessage = "Cancelled.";
            return;
        }
        Execute(new CastCommand(spell, Direction: dir, AllowOverexert: _overexertAfterAim));
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
