using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's multiclasses: a Warrior who also casts a Mage's, Priest's, Druid's or Necromancer's spells.</summary>
public class MulticlassTests
{
    private static GameData Data => TestData.Game;
    private static ClassDef Class(string id) => Data.Class(id)!;

    [Fact]
    public void There_is_a_warrior_pair_for_each_caster_and_none_else()
    {
        Assert.Equal(["warrior_mage", "warrior_priest", "warrior_druid", "warrior_necromancer"], Data.Multiclasses.Select(c => c.Id));
        Assert.Equal("Warrior/Mage", Class("warrior_mage").Name);
        Assert.All(Data.Multiclasses, c => Assert.True(c.IsMulticlass));
        Assert.DoesNotContain(Data.Classes, c => c.IsMulticlass); // (Angband's nine are as they were)
    }

    [Fact]
    public void A_pair_is_halfway_between_its_two_classes()
    {
        var (warrior, mage, pair) = (Class("warrior"), Class("mage"), Class("warrior_mage"));
        Assert.Equal((warrior.HitDie + mage.HitDie + 1) / 2, pair.HitDie);
        foreach (var skill in warrior.Skills.Keys)
            Assert.InRange(pair.Skills[skill], Math.Min(warrior.Skills[skill], mage.Skills.GetValueOrDefault(skill)),
                Math.Max(warrior.Skills[skill], mage.Skills.GetValueOrDefault(skill)));
        Assert.Equal((warrior.MaxAttacks + mage.MaxAttacks + 1) / 2, pair.MaxAttacks);
        Assert.Equal(mage.Realm, pair.Realm);
        Assert.Equal(mage.SpellWeight, pair.SpellWeight);
        Assert.Contains("SHIELD_BASH", pair.Flags);
        Assert.Contains("CHOOSE_SPELLS", pair.Flags);
        Assert.DoesNotContain("ZERO_FAIL", pair.Flags);
        Assert.Equal(Multiclass.ExpPenalty, pair.ExpFactor - Math.Max(warrior.ExpFactor, mage.ExpFactor));
    }

    [Theory]
    [InlineData("warrior_mage", "first_spells")]
    [InlineData("warrior_priest", "novices_handbook")]
    [InlineData("warrior_druid", "lesser_charms")]
    [InlineData("warrior_necromancer", "into_the_shadows")]
    public void A_new_pair_has_the_warriors_kit_and_the_casters_first_book(string cls, string book)
    {
        var game = GameSession.NewGame(Data, 7, CharacterSpec.Default("human", cls));
        Assert.Equal(cls, game.Player.Class!.Id);
        Assert.NotNull(game.Player.Inventory.InSlot(EquipSlot.Body)); // the warrior's armour
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == book);
        Assert.Equal(100 + Multiclass.ExpPenalty + Class("warrior").ExpFactor, game.Player.ExpFactor); // a Human: 150%
        Assert.True(game.Player.MaxMana > 0 || game.Player.Class.FirstSpellLevel > 1);
    }

    [Fact]
    public void A_pair_learns_each_spell_later_and_casts_it_weaker()
    {
        var game = GameSession.NewGame(Data, 7, CharacterSpec.Default("human", "warrior_mage"));
        var mage = GameSession.NewGame(Data, 7, CharacterSpec.Default("human", "mage"));
        foreach (var spell in game.ClassSpells)
        {
            var own = mage.SpellInfo(spell)!.Level;
            Assert.Equal(Math.Min(50, Math.Max(1, own * Multiclass.SpellLevelPercent / 100)), game.SpellInfo(spell)!.Level);
        }
        foreach (var g in new[] { game, mage }) g.GainExperience(g.ExperienceForLevel(29) - g.Player.Experience);
        Assert.Equal(30, game.Player.Level);
        Assert.Equal(30 * Multiclass.CasterLevelPercent / 100, game.CasterLevel);
        Assert.Equal(30, mage.CasterLevel);
        Assert.True(game.Player.MaxMana < mage.Player.MaxMana);
        // It casts what it has learned.
        Assert.True(game.Execute(new StudyCommand("magic_missile")));
        Assert.Contains("magic_missile", game.Player.LearnedSpells);
    }

    [Fact]
    public void A_pair_is_kept_in_the_save()
    {
        var game = GameSession.NewGame(Data, 7, CharacterSpec.Default("dwarf", "warrior_priest"));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(Data, stream);
        Assert.Equal("warrior_priest", loaded.Player.Class!.Id);
        Assert.Equal("Warrior/Priest", loaded.Player.Class.Name);
    }
}
