using System.IO.Compression;
using System.Text.Json.Nodes;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>The book bag: every spellbook carried shares one pack slot.</summary>
public partial class AvaQuestTests
{
    /// <summary>Where a thing sorts in the pack: its own things, the book bag, the gem pouch, the quest satchel.</summary>
    private static int BagRank(Item i) => i.IsQuestItem ? 3 : Inventory.InPouch(i) ? 2 : Inventory.InBookBag(i) ? 1 : 0;

    private static string[] BookKinds(Quester q) =>
        [.. q.Game.Data.Objects.Where(k => k.Base is "magic_book" or "prayer_book" or "nature_book" or "shadow_book").Select(k => k.Id).Take(8)];

    [Fact]
    public void All_the_spellbooks_together_take_one_pack_slot()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        var books = BookKinds(q);
        Assert.True(books.Length >= 4);
        var before = inv.SlotsUsed;
        inv.Add(q.Game.Objects.Create(books[0]));
        Assert.Equal(before + 1, inv.SlotsUsed);                               // the bag's one slot
        foreach (var kind in books.Skip(1)) inv.Add(q.Game.Objects.Create(kind));
        Assert.Equal(before + 1, inv.SlotsUsed);                               // however many, of any realm
        Assert.Equal(books.Length, inv.BookBag.Count());
        Gem(q, "ruby");
        Assert.Equal(before + 2, inv.SlotsUsed);                               // the gem pouch is a slot of its own

        // Order: the pack's own things, the book bag, the gem pouch, the satchel.
        inv.Add(q.Game.Objects.Create("palantir"));
        inv.Add(q.Game.Objects.Create("flask_of_oil"));
        var ranks = inv.Pack.Select(BagRank).ToList();
        Assert.Equal(ranks.Order().ToList(), ranks);

        // A full pack still takes another book, the bag being carried.
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        Assert.Equal(inv.PackSize, inv.SlotsUsed);
        Assert.True(inv.CanCarry(q.Game.Objects.Create(books[0])));
        Assert.False(inv.CanCarry(q.Game.Objects.Create("flask_of_whisky")));
    }

    [Fact]
    public void A_full_pack_with_no_books_yet_cannot_take_the_first()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        Assert.Empty(inv.BookBag);                                             // (a warrior: no books)
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        Assert.False(inv.CanCarry(q.Game.Objects.Create(BookKinds(q)[0])));      // (the bag itself needs a slot)
    }

    [Fact]
    public void An_older_saves_books_go_into_the_bag_on_loading()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        var books = BookKinds(q).Take(4).ToArray();
        foreach (var kind in books) inv.Add(q.Game.Objects.Create(kind));
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        var carried = inv.Pack.Sum(i => i.Number);

        // Put the books where the old order had them: first, among the pack's things.
        using var stream = new MemoryStream();
        SaveGame.Save(q.Game, stream);
        stream.Position = 0;
        JsonNode file;
        using (var unzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(unzip)!;
        var pack = FindArray(file, "Pack")!;
        var bookNodes = pack.Where(n => books.Contains(n!["Kind"]?.GetValue<string>())).ToList();
        Assert.Equal(books.Length, bookNodes.Count);
        foreach (var n in bookNodes) pack.Remove(n);
        foreach (var n in bookNodes.AsEnumerable().Reverse()) pack.Insert(0, n!.DeepClone());
        var old = new MemoryStream();
        using (var zip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(zip)) file.WriteTo(writer);
        old.Position = 0;

        var loaded = SaveGame.Load(TestData.Game, old).Player.Inventory;
        Assert.Equal(books.Order(), loaded.BookBag.Select(i => i.Kind.Id).Order());
        var ranks = loaded.Pack.Select(BagRank).ToList();
        Assert.Equal(ranks.Order().ToList(), ranks);
        Assert.Equal(carried, loaded.Pack.Sum(i => i.Number));                     // nothing lost
        Assert.Equal(inv.SlotsUsed, loaded.SlotsUsed);
    }
}
