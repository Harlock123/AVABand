using Angband.Core.Definitions;
using Angband.Data;

namespace Angband.Tests;

internal static class TestData
{
    private static readonly Lazy<GameData> Lazy = new(() => DataLoader.Load(DataLoader.DefaultDataDirectory));

    /// <summary>The shipped game data (copied next to the test assembly).</summary>
    public static GameData Game => Lazy.Value;
}
