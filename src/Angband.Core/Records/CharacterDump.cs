using System.Globalization;
using System.Text;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Magic;

namespace Angband.Core.Records;

/// <summary>
/// The plain-text character sheet Angband writes as a "character dump": the character, stats, skills,
/// resistances, equipment, pack, quiver, home, spells, killed uniques and the last messages. Only what
/// the player knows is shown (unknown runes appear as <c>?</c>).
/// </summary>
public static class CharacterDump
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(GameSession game, IEnumerable<string>? recentMessages = null, DateTime? when = null)
    {
        var p = game.Player;
        var data = game.Data;
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        void Section(string title)
        {
            Line();
            Line($"  [{title}]");
            Line();
        }

        Line($"  [AVABand character dump — {(when ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm", Inv)}]");
        Line();

        // --- Summary block (two columns, as in Angband's 'C' screen) ---
        var feet = data.Constants.FeetPerLevel;
        var next = p.Level >= StatTables.MaxLevel ? "max" : game.ExperienceForLevel(p.Level).ToString(Inv);
        string[] left =
        [
            $"Name   {p.Name}",
            $"Race   {p.Race?.Name ?? "-"}",
            $"Class  {p.Class?.Name ?? "-"}",
            $"Level  {p.Level}" + (p.MaxLevel > p.Level ? $" (max {p.MaxLevel})" : ""),
            $"Exp    {p.Experience}",
            $"Next   {next}",
            $"Gold   {p.Gold}",
            $"Burden {p.Inventory.TotalWeight / 10}.{p.Inventory.TotalWeight % 10} lb (slowed past {p.WeightLimit / 20}.{p.WeightLimit / 2 % 10})",
        ];
        string[] right =
        [
            $"HP     {p.Hp}/{p.MaxHp}",
            $"SP     {p.Mana}/{p.MaxMana}",
            $"Armour {p.Armour}",
            $"Fight  ({Signed(p.ToHit)},{Signed(p.ToDam)})",
            $"Speed  {(p.Speed == 0 ? "Normal" : Signed(p.Speed))}",
            $"Depth  {(p.Depth == 0 ? "Town" : $"{p.Depth * feet} ft (L{p.Depth})")}",
            $"Max    {p.MaxDepth * feet} ft (L{p.MaxDepth})",
            $"Turns  {game.NormalTurns} (game {game.GameTurn})",
        ];
        for (var i = 0; i < left.Length; i++) Line($" {left[i],-36}{right[i]}");
        // Angband show_title: the class's title for the level (a winner is a winner).
        var titles = p.Class?.Titles ?? [];
        var title = p.IsWinner ? "***WINNER***" : titles.Count > 0 ? titles[Math.Min(titles.Count - 1, (p.Level - 1) / 5)] : null;
        if (title is not null) Line($" Title  {title}");
        if (p.Age > 0) Line($" Age    {p.Age,-29}Height {p.Height / 12}'{p.Height % 12}\"   Weight {p.Weight} lb");
        Line();
        // Angband's character history (the background rolled at birth), wrapped as its 'C' screen does.
        if (p.Background.Length > 0)
        {
            foreach (var line in Wrap(p.Background, 74)) Line($" {line}");
            Line();
        }
        if (p.IsWinner) Line(" *** Winner: slayer of Morgoth ***");
        if (game.PlayerShape is { } shape) Line($" In the shape of {(("aeiou".Contains(char.ToLowerInvariant(shape.Name[0]))) ? "an" : "a")} {shape.Name}.");
        Line(p.IsDead && p.IsWinner ? " Retired victorious."
            : p.IsDead ? $" Killed by {p.KilledBy} {(p.Depth == 0 ? "in the town" : $"at {p.Depth * feet} ft")}."
            : $" Still alive ({game.HungerLevel.ToString().ToLowerInvariant()}).");
        Line($" Seed {game.Seed}   Score {Scoring.Points(p)} points");
        if (p.HeroicBirth) Line($" Heroic: born with AVABand's heroic stats (the score counts {Scoring.HeroicPercent}%).");
        if (game.IsCheater) Line($" Cheated ({string.Join(", ", game.CheatsUsed.Order(StringComparer.Ordinal))}): not scored.");

        // --- Abilities (Angband's birth screen names them; player_property.txt) ---
        var abilities = Birth.Abilities(data, p.Race, p.Class);
        if (abilities.Count > 0)
        {
            Section("Abilities");
            foreach (var (name, description) in abilities) Line($" {name + ':',-24} {description}");
        }
        // AVABand's own racial ability (birth_ava_races).
        if (p.Race is { AvaAbilityText.Length: > 0 } race && game.Options[OptionIds.AvaRaces])
        {
            Section("Racial ability (AVABand)");
            Line(" " + race.AvaAbilityText);
        }

        // --- Birth options (as in Angband's dump) ---
        Section("Birth options");
        foreach (var o in OptionCatalog.OfKind(OptionKind.Birth))
            Line($" {o.Description,-46}: {(game.Options[o.Id] ? "yes" : "no")} ({o.Id})");

        // --- Stats ---
        Section("Stats");
        Line("        Base  Race  Class  Current");
        foreach (var stat in CharacterSpec.StatIds)
        {
            var raceMod = p.Race?.Stats.GetValueOrDefault(stat) ?? 0;
            var classMod = p.Class?.Stats.GetValueOrDefault(stat) ?? 0;
            var baseValue = p.BaseStats.GetValueOrDefault(stat, p.Stats.GetValueOrDefault(stat) - raceMod - classMod);
            Line(string.Create(Inv, $" {stat.ToUpperInvariant(),-4}  {StatTables.Format(baseValue),6}  {Signed(raceMod),4}  {Signed(classMod),5}  {StatTables.Format(p.Stats.GetValueOrDefault(stat)),7}"));
        }

        // --- Skills ---
        Section("Skills");
        var blows = p.Blows / 100.0;
        var shots = p.Shots / 10.0;
        Line(string.Create(Inv, $" Melee    {p.SkillMelee,4}   Blows   {blows:0.0}/turn   Infravision {p.TotalInfravision * 10} ft"));
        Line(string.Create(Inv, $" Shooting {p.SkillBow,4}   Shots   {shots:0.0}/turn   Light       {p.LightRadius}"));
        Line(string.Create(Inv, $" Throwing {p.SkillThrow,4}   Saving  {p.SkillSave}%       Stealth     {p.Stealth}"));
        Line(string.Create(Inv, $" Disarm   {p.DisarmSkill,4}   Devices {p.SkillDevice,4}       Digging     {game.DiggingSkill}"));
        Line(string.Create(Inv, $" (magic)  {p.DisarmMagicSkill,4}   Search  {game.SearchSkill,4}"));

        // --- Resistances grid ---
        Section("Resistances");
        var equipment = p.Inventory.Equipment;
        var header = new StringBuilder("            ");
        for (var i = 0; i < equipment.Count; i++) header.Append(Letter(i));
        header.Append(" @  Total");
        Line(header.ToString());
        foreach (var element in data.Elements.Where(e => e.Resistable))
        {
            var rune = RuneIds.Resist(element.Id);
            var known = game.Knowledge.KnowsRune(rune);
            var row = new StringBuilder($" {Truncate(Capitalize(element.Name), 10),-10} ");
            foreach (var item in equipment)
                row.Append(item is null ? ' ' : !known ? '?' : item.Resists.Contains(element.Id) ? '+' : '.');
            row.Append(p.IntrinsicResists.GetValueOrDefault(element.Id) > 0 ? " +" : " .");
            var total = p.Resists.GetValueOrDefault(element.Id);
            var anyUnknown = !known && equipment.Any(i => i is not null && i.Resists.Contains(element.Id));
            row.Append("  ").Append(anyUnknown ? "?" : total switch { >= 3 => "Immune", 2 => "Double", 1 => "Resist", < 0 => "Vulnerable", _ => "-" });
            Line(row.ToString());
        }

        // --- Items ---
        Section("Equipment");
        for (var i = 0; i < equipment.Count; i++)
        {
            var slot = Inventory.Slots[i];
            Line($" {Letter(i)}) {Capitalize(slot.Name) + ':',-14} {(equipment[i] is { } item ? game.Describe(item) : "(nothing)")}");
        }

        Section("Inventory");
        if (p.Inventory.Pack.Count == 0) Line(" (empty)");
        for (var i = 0; i < p.Inventory.Pack.Count; i++) Line($" {Letter(i)}) {game.Describe(p.Inventory.Pack[i])}");

        if (p.Inventory.Quiver.Count > 0)
        {
            Section("Quiver");
            for (var i = 0; i < p.Inventory.Quiver.Count; i++) Line($" {i}) {game.Describe(p.Inventory.Quiver[i])}");
        }

        if (game.Stores.Values.FirstOrDefault(s => s.IsHome) is { Stock.Count: > 0 } home)
        {
            Section("Home");
            for (var i = 0; i < home.Stock.Count; i++) Line($" {Letter(i)}) {game.Describe(home.Stock[i])}");
        }

        // --- Spells ---
        var spells = game.ClassSpells.Where(s => p.LearnedSpells.Contains(s.Id)).ToList();
        if (spells.Count > 0)
        {
            Section("Spells");
            Line(" Name                          Lv Mana Fail");
            foreach (var spell in spells)
            {
                var info = game.SpellInfo(spell)!;
                Line(string.Create(Inv, $" {Truncate(spell.Name, 29),-29} {info.Level,2} {info.Mana,4} {game.SpellFailChance(spell),3}%{(p.CastSpells.Contains(spell.Id) ? "" : "  (untried)")}"));
            }
        }

        if (data.Quests.Count > 0)
        {
            Section("Quests");
            foreach (var q in data.Quests)
                Line($" {q.Name,-10} {q.Level * feet,5} ft   {(game.IsQuestComplete(q) ? "completed" : "not yet")}");
        }

        if (game.KilledUniques.Count > 0)
        {
            Section("Uniques slain");
            foreach (var id in game.KilledUniques.Order(StringComparer.Ordinal))
                Line($" {data.Monster(id)?.Name ?? id}");
        }

        if (game.History.Count > 0)
        {
            // Angband dump_history.
            Section("Player history");
            Line("      Turn   Depth  Note");
            foreach (var h in game.History)
                Line($"{h.Turn,10}{h.Depth * 50,7}'  {h.Shown}");
        }

        var messages = recentMessages?.ToList();
        if (messages is { Count: > 0 })
        {
            Section("Last messages");
            foreach (var m in messages) Line($" {m}");
        }
        return sb.ToString();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static char Letter(int i) => (char)('a' + i);

    private static string Signed(int v) => v >= 0 ? "+" + v.ToString(Inv) : v.ToString(Inv);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    /// <summary>Words to lines no longer than <paramref name="width"/>.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
