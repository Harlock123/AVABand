using Angband.Core.Definitions;
using Angband.Core.Monsters;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Core.Game;

// AVABand's own: danger at a glance. The monster list tags a monster that could kill you, or take half
// your life at once, as far as you know of it (the recall's Danger line says why), or that's far out of
// depth; and the first time a deadly or far-out-of-depth kind comes into view on a level, you're warned.
public sealed partial class GameSession
{
    /// <summary>A monster whose native depth is at least this many levels below this one is far out of depth.</summary>
    public const int FarOutOfDepth = 10;

    private Level? _bewareLevel;
    private readonly HashSet<string> _bewared = [];

    /// <summary>Far deeper than it belongs here (and not just deeper than you are used to).</summary>
    public bool IsFarOutOfDepth(MonsterRaceDef race) => Level.Depth > 0 && race.Depth >= Level.Depth + FarOutOfDepth;

    /// <summary>The monster list's tag: "could kill you", "dangerous", "far out of depth", or none.</summary>
    public string? DangerTag(MonsterRaceDef race) => DangerWord(race) ?? (IsFarOutOfDepth(race) ? "far out of depth" : null);

    /// <summary>The first time a deadly or far-out-of-depth kind comes into view on a level: a warning.</summary>
    private void Beware(Monster m)
    {
        if (!ReferenceEquals(_bewareLevel, Level))
        {
            _bewareLevel = Level;
            _bewared.Clear();
        }
        if (IsHallucinating || !_bewared.Add(m.Race.Id)) return;
        var name = MonsterName(m);
        var danger = MonsterRecall.Danger(Data, m.Race, Lore.Find(m.Race.Id) ?? new RaceLore(), RecallViewer);
        if (danger.Level == MonsterRecall.DangerLevel.Deadly)
            Publish(new MessageEvent($"Beware: {name} could kill you — its {danger.What} can do up to {danger.Damage}."));
        else if (IsFarOutOfDepth(m.Race))
            Publish(new MessageEvent($"Beware: {name} is far deeper than it belongs."));
    }
}
