using Angband.Core.Definitions;
using Angband.Core.Monsters;
using Angband.Core.Records;

namespace Angband.Core.Game;

// Monster memory (Angband lore.c): sightings, kills, deaths, attacks and spells seen, and traits
// learned in play. The book is shared across characters; the application loads and saves it.
public sealed partial class GameSession
{
    /// <summary>The player's monster memory (replace it with a loaded book to keep lore across games).</summary>
    public MonsterLoreBook Lore { get; set; } = new();

    /// <summary>This character's kills, by race (saved with the character).</summary>
    public Dictionary<string, int> CharacterKills { get; } = new(StringComparer.Ordinal);

    // The monster whose attack or spell is being resolved, so a death can be put on its record.
    private string? _attacker;

    /// <summary>Traits obvious at a glance (Angband's "obvious" flags).</summary>
    private static readonly string[] ObviousFlags = [MonsterFlags.Unique, MonsterFlags.Male, MonsterFlags.Female, MonsterFlags.NeverMove];

    /// <summary>What kind of creature it was is plain once one lies dead.</summary>
    private static readonly string[] CorpseFlags = ["ANIMAL", "ORC", MonsterFlags.Troll, MonsterFlags.Giant, MonsterFlags.Dragon, MonsterFlags.Demon, "UNDEAD"];

    /// <summary>The recall text for a race, from the current memory.</summary>
    public string Recall(MonsterRaceDef race) =>
        MonsterRecall.Describe(Data, race, Lore.Find(race.Id), Player.Level, CharacterKills.GetValueOrDefault(race.Id), RecallViewer);

    /// <summary>The recall with Angband's colours marked (<see cref="RecallMarkup"/>).</summary>
    public string RecallMarked(MonsterRaceDef race) =>
        MonsterRecall.DescribeMarked(Data, race, Lore.Find(race.Id), Player.Level, CharacterKills.GetValueOrDefault(race.Id), RecallViewer);

    /// <summary>The player as monster recall sees them: known resistances and saving throw.</summary>
    public RecallViewer RecallViewer => new(KnownResist, Player.SkillSave, Player.Hp, Player.MaxHp);

    /// <summary>
    /// A resistance level as far as the player knows it (Angband known_state): a resistance that
    /// only comes from gear whose rune hasn't been learned doesn't count.
    /// </summary>
    public int KnownResist(string id)
    {
        var level = Player.Resists.GetValueOrDefault(id);
        if (level <= 0 || Knowledge.KnowsRune(RuneIds.Resist(id))) return level;
        var fromGear = Player.Inventory.Equipped.Any(i => i.Resists.Contains(id));
        return fromGear ? level - 1 : level;
    }

    /// <summary>Learns a trait of a race if it really has it.</summary>
    public void LearnMonsterFlag(MonsterRaceDef race, string flag)
    {
        if (race.Has(flag)) Lore.For(race.Id).FlagsKnown.Add(flag);
    }

    /// <summary>
    /// Angband project_m: a visible monster hit by an element shows whether it has the matching
    /// resistance or vulnerability — either way, the player learns something.
    /// </summary>
    public void LearnMonsterResponse(Monster monster, string flag)
    {
        if (!monster.IsVisible) return;
        var lore = Lore.For(monster.Race.Id);
        if (monster.Race.Has(flag)) lore.FlagsKnown.Add(flag);
        else lore.FlagsLacking.Add(flag);
    }

    /// <summary>Learns that a race has a spell (it showed it by resisting what it breathes).</summary>
    public void LearnMonsterSpell(MonsterRaceDef race, string spell) => Lore.For(race.Id).SpellsSeen.Add(spell);

    /// <summary>
    /// AVABand: what you know of a race says it's a danger to you — "could kill you" (its worst known
    /// attack could take all your hit points), "dangerous" (half of them) — or null. The recall's
    /// Danger line says why.
    /// </summary>
    public string? DangerWord(MonsterRaceDef race) =>
        MonsterRecall.Danger(Data, race, Lore.Find(race.Id) ?? new RaceLore(), RecallViewer).Level switch
        {
            MonsterRecall.DangerLevel.Deadly => "could kill you",
            MonsterRecall.DangerLevel.Serious => "dangerous",
            _ => null,
        };

    /// <summary>Angband look_mon_desc: "the cave orc (wounded, asleep)".</summary>
    public string LookDescription(Monster m)
    {
        if (IsHallucinating) return "something strange"; // Angband aux_hallucinate
        var pct = m.MaxHp <= 0 ? 100 : Math.Max(0, m.Hp) * 100 / m.MaxHp;
        var states = new List<string>
        {
            pct >= 100 ? "unhurt" : pct >= 60 ? "somewhat wounded" : pct >= 25 ? "wounded" : pct >= 10 ? "badly wounded" : "almost dead",
        };
        if (m.IsAsleep) states.Add("asleep");
        if (m.IsAfraid) states.Add("afraid");
        if (m.Confused > 0) states.Add("confused");
        if (m.Stun > 0) states.Add("stunned");
        if (m.Held > 0) states.Add("held");
        if (m.Slow > 0) states.Add("slowed");
        if (m.Fast > 0) states.Add("hasted");
        // AVABand: what you know of it says it's a danger to you (the recall's Danger line says why).
        if (DangerWord(m.Race) is { } danger) states.Add(danger);
        return $"{MonsterName(m)} ({string.Join(", ", states)})";
    }

    private void NoteSighting(Monster m)
    {
        if (m.EverSeen) return;
        m.EverSeen = true;
        var lore = Lore.For(m.Race.Id);
        lore.Sights++;
        if (lore.Sights == 1 && m.Race.Has(MonsterFlags.Unique)) Publish(new UniqueFirstSeenEvent(m));
        foreach (var flag in ObviousFlags) LearnMonsterFlag(m.Race, flag);
        if (m.Race.Has(MonsterFlags.Invisible)) lore.FlagsKnown.Add(MonsterFlags.Invisible);
    }

    private void NoteKill(Monster m, int itemsDropped, int goldDropped)
    {
        var race = m.OriginalRace ?? m.Race;
        var lore = Lore.For(race.Id);
        CharacterKills[race.Id] = CharacterKills.GetValueOrDefault(race.Id) + 1;
        NoteJourneyKill(m);
        lore.TotalKills++;
        foreach (var flag in CorpseFlags) LearnMonsterFlag(race, flag);
        if (m.IsVisible || Level[m.Position].Has(World.SquareFlags.Seen))
        {
            lore.MaxItemsDropped = Math.Max(lore.MaxItemsDropped, itemsDropped);
            lore.MaxGoldDropped = Math.Max(lore.MaxGoldDropped, goldDropped);
        }
    }

    private void NoteDeath()
    {
        if (_attacker is { } id) Lore.For(id).Deaths++;
    }
}
