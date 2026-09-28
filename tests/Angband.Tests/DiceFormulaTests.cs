using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's dice strings with expressions (dice.c, expression.c), as monster spells now give
/// their damage: 4.2.5's own formulas, worked out from the caster's spell power.
/// </summary>
public class DiceFormulaTests
{
    private static readonly Dictionary<string, string> None = [];

    [Theory]
    [InlineData("SPELL_POWER:/ 8 + 1", 40, 6)]
    [InlineData("SPELL_POWER:* 5 / 2", 30, 75)]
    [InlineData("SPELL_POWER:* 3 / 2 + 30", 11, 46)] // left to right, whole numbers: 33/2=16, +30
    [InlineData("SPELL_POWER", 17, 17)]
    [InlineData("MAX_SIGHT:- 5", 99, 15)]
    public void Expressions_apply_left_to_right(string expression, int power, int expected) =>
        Assert.Equal(expected, DiceFormula.Evaluate(expression, power));

    [Fact]
    public void Formulas_resolve_to_base_dice_and_sides()
    {
        Assert.Equal((0, 6, 6), DiceFormula.Resolve("$Dd6", new Dictionary<string, string> { ["D"] = "SPELL_POWER:/ 8 + 1" }, 40));
        Assert.Equal((50, 1, 75), DiceFormula.Resolve("50+1d$S", new Dictionary<string, string> { ["S"] = "SPELL_POWER:* 5 / 2" }, 30));
        Assert.Equal((46, 5, 5), DiceFormula.Resolve("$B+5d5", new Dictionary<string, string> { ["B"] = "SPELL_POWER:* 3 / 2 + 30" }, 11));
        Assert.Equal((0, 1, 20), DiceFormula.Resolve("d$S", new Dictionary<string, string> { ["S"] = "SPELL_POWER" }, 20));
        Assert.Equal((12, 0, 0), DiceFormula.Resolve("12", None, 1));
        Assert.Equal((3, 2, 8), DiceFormula.Resolve("3+2d8", None, 1));
    }

    [Fact]
    public void Rolls_stay_within_the_formula()
    {
        var terms = new Dictionary<string, string> { ["S"] = "SPELL_POWER:* 5 / 2" };
        var rng = new GameRandom(7);
        for (var i = 0; i < 200; i++)
        {
            var roll = DiceFormula.Roll("50+1d$S", terms, 30, rng);
            Assert.InRange(roll, 51, 125);
        }
        Assert.Equal(125, DiceFormula.Max("50+1d$S", terms, 30));
    }

    [Fact]
    public void Monster_spells_take_4_2_5s_formulas()
    {
        var data = Angband.Data.DataLoader.LoadDefault(includeUserMods: false);
        var mana = data.MonsterSpells.First(s => s.Id == "BO_MANA");
        Assert.Equal("50+1d$S", mana.DamageFormula);
        Assert.Equal("SPELL_POWER:* 5 / 2", mana.FormulaTerms["S"]);
    }
}
