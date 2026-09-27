using Angband.Core.Time;

namespace Angband.Tests;

public class EnergyTableTests
{
    [Fact]
    public void Table_Has200Entries() => Assert.Equal(200, EnergyTable.Count);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 20)]
    [InlineData(20, 30)]
    [InlineData(-10, 5)]
    [InlineData(-20, 3)]
    [InlineData(1, 11)]
    public void KnownSpeeds_GiveAngbandEnergy(int speed, int energy) =>
        Assert.Equal(energy, EnergyTable.EnergyPerTurn(speed));

    [Fact]
    public void Energy_IsMonotonic()
    {
        for (var s = EnergyTable.MinSpeed + 1; s <= EnergyTable.MaxSpeed; s++)
            Assert.True(EnergyTable.EnergyPerTurn(s) >= EnergyTable.EnergyPerTurn(s - 1), $"speed {s}");
    }

    [Fact]
    public void ExtremeSpeeds_AreClamped()
    {
        Assert.Equal(1, EnergyTable.EnergyPerTurn(-500));
        Assert.Equal(49, EnergyTable.EnergyPerTurn(500));
    }
}
