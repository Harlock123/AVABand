using Angband.Core.Definitions;

namespace Angband.Core.Game;

// AVABand's own: a breeder race runs out of young on a level. Angband caps only the breeders alive
// at once (repro_monster_max), which a swarm you keep thinning never reaches — so a corridor of lice
// could refill each square you cleared for ever. Here each breeder race has at most
// BirthsPerBreederPerLevel young on a level; then it breeds no more there, and the swarm can be
// cut down. The count starts again whenever you arrive on a level (a new one, or a persistent
// level you come back to).
public sealed partial class GameSession
{
    /// <summary>The most young one breeder race has on a level (counted from your arrival).</summary>
    public const int BirthsPerBreederPerLevel = 250;

    /// <summary>Has this breeder race had all its young on this level?</summary>
    public bool BreederSpent(MonsterRaceDef race) => Level.Births.GetValueOrDefault(race.Id) >= BirthsPerBreederPerLevel;

    private void CountBirth(MonsterRaceDef race) => Level.Births[race.Id] = Level.Births.GetValueOrDefault(race.Id) + 1;
}
