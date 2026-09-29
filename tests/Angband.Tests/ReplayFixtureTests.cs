using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>
/// Whole games the soak test's bot played (Replays/: a warrior, a mage and a ranger from 50 ft, and a
/// warrior's "tourist" run from 3000 ft), each of which must play back to exactly the end it was
/// recorded with. Any change to how the game plays out — a formula, a roll, a new message that uses
/// the dice — shows here. When the change is meant, record them again:
/// <c>dotnet run -c Release --project tools/balance record-replays</c> (and say so in the commit).
/// </summary>
public class ReplayFixtureTests
{
    public static TheoryData<string> Replays()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Replays"), "*" + ReplayFile.Extension).Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Fact]
    public void ThereAreRecordedGames() => Assert.True(Replays().Count >= 4);

    [Theory]
    [MemberData(nameof(Replays))]
    public void A_recorded_game_plays_back_to_its_end(string name)
    {
        var file = ReplayFile.Read(Path.Combine(AppContext.BaseDirectory, "Replays", name));
        Assert.True(file.Steps.Count > 500, $"{file.Steps.Count} steps");
        var player = new ReplayPlayer(TestData.Game, file);
        while (player.Step()) { }
        Assert.True(player.Matches,
            $"{name} was recorded ending {file.End} but now ends {ReplayCodec.EndOf(player.Game)}: the game plays differently. "
            + "If that is meant, re-record: dotnet run -c Release --project tools/balance record-replays");
    }
}
