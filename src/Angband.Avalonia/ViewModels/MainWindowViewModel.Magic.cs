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

    private IReadOnlyList<string>? _studyBooks;

    /// <summary>
    /// Priests and paladins (no CHOOSE_SPELLS) pick a book and are granted one of its spells at
    /// random (Angband do_cmd_study_book); with a single book there's nothing to ask.
    /// </summary>
    private void BeginStudyBookPrompt(string spellNoun)
    {
        var books = _game.StudyableBooks();
        if (books.Count == 0)
        {
            AddMessage($"You cannot learn any new {spellNoun}s right now.");
            return;
        }
        if (books.Count == 1)
        {
            Execute(new StudyCommand(Book: books[0]));
            return;
        }
        _studyBooks = books;
        ChoiceRows.Clear();
        PromptRows.Clear();
        SpellPromptRows.Clear();
        for (var i = 0; i < books.Count; i++)
            ChoiceRows.Add(new ChoiceRow(((char)('a' + i)).ToString(), _data.Object(books[i])?.Name.Replace("~", "").Replace("& ", "") ?? books[i]));
        PromptTitle = "Study which book?";
        IsPrompting = true;
    }

    /// <summary>A letter in the book menu; anything else cancels.</summary>
    private void ChooseStudyBook(char key)
    {
        var books = _studyBooks;
        _studyBooks = null;
        ChoiceRows.Clear();
        var index = key - 'a';
        if (books is null || index < 0 || index >= books.Count)
        {
            LastMessage = "Cancelled.";
            return;
        }
        Execute(new StudyCommand(Book: books[index]));
    }

    public void BeginSpellPrompt(SpellPromptKind kind)
    {
        var realm = _game.PlayerRealm;
        if (realm is null)
        {
            AddMessage("You cannot use magic.");
            return;
        }
        if (kind == SpellPromptKind.Study && !_game.ChoosesSpells)
        {
            BeginStudyBookPrompt(realm.SpellNoun);
            return;
        }

        var spells = kind switch
        {
            SpellPromptKind.Cast => _game.ClassSpells.Where(s => _game.Player.LearnedSpells.Contains(s.Id) && _game.HasBookFor(s)),
            SpellPromptKind.Study => _game.StudyableSpells(),
            _ => _game.ClassSpells.Where(_game.HasBookFor),
        };
        var list = spells.ToList();
        if (list.Count > 0 && OptionValue(DisplayOptions.BookFirst))
        {
            BeginSpellBookPrompt(kind, list);
            return;
        }
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

        ShowSpellRows(kind, list);
    }

    private List<string>? _spellBooks;
    private List<Angband.Core.Definitions.SpellDef> _spellBookChoices = [];

    /// <summary>
    /// Book-first menus (the option): which book, then its spells lettered by their place in it, as
    /// Angband's menus are — so its keymaps (maa') work as written.
    /// </summary>
    private void BeginSpellBookPrompt(SpellPromptKind kind, List<Angband.Core.Definitions.SpellDef> offered)
    {
        _spellPromptKind = kind;
        _spellBookChoices = offered;
        _spellBooks = [.. offered.Select(s => s.Book).Distinct()];
        ChoiceRows.Clear();
        PromptRows.Clear();
        SpellPromptRows.Clear();
        for (var i = 0; i < _spellBooks.Count && i < 26; i++)
            ChoiceRows.Add(new ChoiceRow(((char)('a' + i)).ToString(),
                _data.Object(_spellBooks[i])?.Name.Replace("~", "").Replace("& ", "") ?? _spellBooks[i]));
        PromptTitle = kind switch
        {
            SpellPromptKind.Cast => $"{Capitalize(_game.PlayerRealm!.Verb)} from which book?",
            SpellPromptKind.Study => "Study from which book?",
            _ => "Browse which book?",
        };
        IsPrompting = true;
    }

    /// <summary>A letter in the book menu: that book's spells, in book order (those not on offer can't be chosen).</summary>
    private void ChooseSpellBook(char key)
    {
        var books = _spellBooks;
        _spellBooks = null;
        ChoiceRows.Clear();
        var index = key - 'a';
        if (books is null || index < 0 || index >= books.Count)
        {
            LastMessage = "Cancelled.";
            return;
        }
        var offered = _spellBookChoices.Select(s => s.Id).ToHashSet();
        ShowSpellRows(_spellPromptKind, [.. _game.ClassSpells.Where(s => s.Book == books[index])], offered);
    }

    /// <summary>The spell menu itself; with <paramref name="offered"/>, spells outside it are listed but not chosen.</summary>
    private void ShowSpellRows(SpellPromptKind kind, List<Angband.Core.Definitions.SpellDef> list, HashSet<string>? offered = null)
    {
        var realm = _game.PlayerRealm!;
        _spellPromptKind = kind;
        _spellOffered = offered;
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

    private HashSet<string>? _spellOffered;

    /// <summary>Picks a spell from the open spell list.</summary>
    private void ChooseSpell(SpellRow row)
    {
        var spell = row.Spell;
        if (_spellOffered is { } offered && !offered.Contains(spell.Id) && _spellPromptKind != SpellPromptKind.Browse)
        {
            AddMessage(_spellPromptKind == SpellPromptKind.Study
                ? $"You cannot learn that {_game.PlayerRealm!.SpellNoun} yet."
                : $"You don't know that {_game.PlayerRealm!.SpellNoun}.");
            return;
        }
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
        if (_data.Spell(spellId) is { } s && GameSession.NeedsCurseChoice(s.Effect))
        {
            BeginUncurse(choice => new CastCommand(spellId, AllowOverexert: allowOverexert, Uncurse: choice),
                _game.UncurseStrengthText(s.Effect));
            return;
        }
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
