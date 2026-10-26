using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>Unlocks: rewards earned through feats, and the alternative starting kits (worth no more than the standard).</summary>
public class UnlockTests
{
    private static readonly IReadOnlyList<FeatDef> AllFeats = Feats.All(TestData.Game);

    private static FeatBook Book(params string[] ids)
    {
        var book = new FeatBook();
        foreach (var id in ids) book.Earned[id] = new FeatRecord { Character = "Tester" };
        return book;
    }

    [Fact]
    public void EveryUnlock_IsNamedOnce_AndNeedsFeatsThatExist()
    {
        Assert.Equal(Unlocks.All.Count, Unlocks.All.Select(u => u.Id).Distinct().Count());
        Assert.InRange(Unlocks.All.Count, 10, 14);
        foreach (var u in Unlocks.All)
        {
            Assert.True(u.Feats.Count > 0 || u.FeatCount > 0, u.Id);
            Assert.All(u.Feats, id => Assert.Contains(AllFeats, f => f.Id == id));
        }
        Assert.All(Unlocks.All.Where(u => u.Kind == UnlockKind.StartingKit), u => Assert.NotNull(StartingKits.Find(u.Key)));
    }

    [Fact]
    public void Earned_FollowsTheFeatsDone()
    {
        Assert.Empty(Unlocks.Earned(new FeatBook()));
        Assert.Equal(["title_shaft"], Unlocks.Earned(Book("depth_10", "depth_20")).Select(u => u.Id));
        Assert.Equal(["frame_trophy", "frame_iron"], Unlocks.Earned(Book("uniques_1", "challenge_ironman")).Select(u => u.Id));
        Assert.Contains(Unlocks.Earned(Book("win")), u => u.Id == "frame_gold");
        Assert.Contains(Unlocks.Earned(Book("depth_40")), u => u.Id == "kit_delver");
        Assert.Contains(Unlocks.Earned(Book("level_20")), u => u.Id == "kit_scholar");

        // The collector's frames come with a number of feats, of any kind.
        var five = Book("depth_10", "level_10", "gold", "artifact", "board");
        Assert.Equal(["frame_silver"], Unlocks.Earned(five).Select(u => u.Id));
        var fifteen = Book([.. AllFeats.Where(f => !f.Id.StartsWith("depth") && !f.Id.StartsWith("uniques") && !f.Id.StartsWith("win")
                                                      && f.Id is not ("ghost" or "challenge_ironman" or "daily" or "level_20" or "ava_quest" or "heart_returned"))
                                 .Take(15).Select(f => f.Id)]);
        Assert.Equal(["frame_silver", "frame_elven"], Unlocks.Earned(fifteen).Select(u => u.Id));
        Assert.Equal(Unlocks.All.Count, Unlocks.Earned(Book([.. AllFeats.Select(f => f.Id)])).Count);
    }

    [Fact]
    public void NewlyEarned_IsOnlyWhatTheFreshFeatsUnlocked()
    {
        var book = Book("depth_10", "level_10", "gold", "artifact");
        var fresh = AllFeats.Where(f => f.Id is "depth_20" or "board").ToList();
        foreach (var f in fresh) book.Earned[f.Id] = new FeatRecord();
        Assert.Equal(["title_shaft", "frame_silver"], Unlocks.NewlyEarned(book, fresh).Select(u => u.Id));
        Assert.Empty(Unlocks.NewlyEarned(book, []));
    }

    [Fact]
    public void Requirement_NamesTheFeat_OrTheCount()
    {
        var shaft = Unlocks.All.Single(u => u.Id == "title_shaft");
        Assert.Equal("Earned by the feat “Into the Deep” (Reach 1000 ft).", shaft.Requirement(AllFeats));
        Assert.Equal("Earned by 15 feats of any kind.", Unlocks.All.Single(u => u.Id == "frame_elven").Requirement(AllFeats));
    }

    // --- The alternative kits -------------------------------------------------------------------

    private static long Worth(IEnumerable<StartItemDef> lines) =>
        lines.Sum(l => TestData.Game.Object(l.Kind) is { } kind
            ? ItemValue.Real(new ObjectFactory(TestData.Game).Create(kind, l.Count), TestData.Game, l.Count) : 0);

    [Fact]
    public void EachKit_IsWorthNoMoreThanTheWordOfRecall_ItTrades()
    {
        var recall = Worth([new StartItemDef { Kind = StartingKits.TradedKind }]);
        Assert.True(recall > 0);
        foreach (var kit in StartingKits.All)
        {
            Assert.All(kit.Gives, g => Assert.NotNull(TestData.Game.Object(g.Kind)));
            var worth = Worth(kit.Gives);
            Assert.InRange(worth, recall - 10, recall); // close to it, never above
        }
    }

    public static TheoryData<string, string> KitsByClass()
    {
        var data = new TheoryData<string, string>();
        foreach (var cls in TestData.Game.Classes.Concat(TestData.Game.Multiclasses))
            foreach (var kit in StartingKits.All)
                data.Add(cls.Id, kit.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(KitsByClass))]
    public void AnAlternativeKit_LeavesAtLeastTheStandardGold(string classId, string kitId)
    {
        var spec = CharacterSpec.Default("human", classId);
        var standard = GameSession.NewGame(TestData.Game, 7, spec);
        var alt = GameSession.NewGame(TestData.Game, 7, spec with { Kit = kitId });
        Assert.True(alt.Player.Gold >= standard.Player.Gold, $"{classId}/{kitId}: {alt.Player.Gold} < {standard.Player.Gold}");
        // ...and the total (gear and gold) is no more than the standard's: no gain, just a different choice.
        long Total(GameSession g) => g.Player.Gold + g.Player.Inventory.All.Sum(i => ItemValue.Real(i, g.Data, i.Number));
        Assert.True(Total(alt) <= Total(standard) + 5, $"{classId}/{kitId}: {Total(alt)} > {Total(standard)}");

        var kit = StartingKits.Find(kitId)!;
        bool Has(GameSession g, string kind) => g.Player.Inventory.All.Any(i => i.Kind.Id == kind);
        Assert.True(Has(standard, StartingKits.TradedKind));
        Assert.False(Has(alt, StartingKits.TradedKind));
        Assert.All(kit.Gives, g => Assert.True(Has(alt, g.Kind), g.Kind));
        // The rest of the class's kit (weapon, armour, books) is the same.
        var cls = TestData.Game.Class(classId) ?? TestData.Game.Multiclasses.First(c => c.Id == classId);
        Assert.All(cls.StartingKit.Where(k => k.Kind != StartingKits.TradedKind && k.Kind != "wooden_torch" && k.Kind != "ration_of_food"
                                                && TestData.Game.Object(k.Kind) is not null),
            k => Assert.True(Has(alt, k.Kind), k.Kind));
    }

    [Fact]
    public void WithoutRecall_TheKitTradesNothing()
    {
        var options = OptionCatalog.OfKind(OptionKind.Birth).ToDictionary(o => o.Id, o => o.Default);
        options[OptionIds.NoRecall] = true;
        var spec = CharacterSpec.Default("human", "warrior") with { Options = options };
        var standard = GameSession.NewGame(TestData.Game, 3, spec);
        var alt = GameSession.NewGame(TestData.Game, 3, spec with { Kit = "delver" });
        Assert.Equal(standard.Player.Gold, alt.Player.Gold);
        Assert.DoesNotContain(alt.Player.Inventory.All, i => i.Kind.Id == "phase_door");
    }

    [Fact]
    public void NoKit_IsTheStandardKit_AsTheDailyDungeonUses()
    {
        var cls = TestData.Game.Class("warrior")!;
        Assert.Same(cls.StartingKit, StartingKits.For(cls, null));
        Assert.Same(cls.StartingKit, StartingKits.For(cls, "no_such_kit"));
        Assert.Null(DailyDungeon.SpecFor(TestData.Game, new DateOnly(2026, 10, 26), "Daily").Kit);
    }
}
