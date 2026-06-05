using System;
using System.IO;
using System.Linq;
using Biggy;
using Xunit;

namespace Biggy.BiggyList.Characterization {

  // Contract-compliant item type: overrides Equals AND GetHashCode consistently
  // (both keyed on Sku). This is the contract BiggyList requires once it uses a
  // hash-based membership index (F4).
  public class Product {
    public string Sku { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }

    public override bool Equals(object obj) {
      var p1 = obj as Product;
      return p1 != null && this.Sku == p1.Sku;
    }
    public override int GetHashCode() {
      return Sku == null ? 0 : Sku.GetHashCode();
    }
  }

  // Non-compliant type: overrides Equals WITHOUT GetHashCode. Used to document
  // the deliberate contract change in F4 (see the "contract" test below).
  public class LooseProduct {
    public string Sku { get; set; }
    public string Name { get; set; }
    public override bool Equals(object obj) {
      var p1 = obj as LooseProduct;
      return p1 != null && this.Sku == p1.Sku;
    }
    // no GetHashCode override (intentional)
  }

  public class BiggyListCharacterization : IDisposable {
    private readonly string _dir;

    public BiggyListCharacterization() {
      // Each test gets an isolated temp directory so the JSON store never clashes.
      _dir = Path.Combine(Path.GetTempPath(), "biggy-char-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_dir);
    }

    public void Dispose() {
      try { Directory.Delete(_dir, true); } catch { }
    }

    private BiggyList<T> NewList<T>(string name) {
      return new BiggyList<T>(dbPath: _dir, dbName: name);
    }

    // ---------- upsert / ordering ----------

    [Fact]
    public void Add_distinct_items_appends_in_insertion_order() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A", Name = "a" });
      list.Add(new Product { Sku = "B", Name = "b" });
      list.Add(new Product { Sku = "C", Name = "c" });

      Assert.Equal(3, list.Count);
      Assert.Equal(new[] { "A", "B", "C" }, list.Select(p => p.Sku));
    }

    [Fact]
    public void Add_equal_item_upserts_in_place_latest_wins() {
      // Mirrors Tests/Writes.cs "WontDuplicate": Equals is keyed on Sku, so adding
      // a second item with the same Sku replaces rather than duplicates.
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "XXX", Name = "first" });
      list.Add(new Product { Sku = "XXX", Name = "second" });

      Assert.Equal(1, list.Count);
      Assert.Equal("second", list.First().Name);
    }

    [Fact]
    public void Update_existing_replaces_in_place_preserving_position() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A", Name = "a" });
      list.Add(new Product { Sku = "B", Name = "b" });
      list.Add(new Product { Sku = "C", Name = "c" });

      list.Add(new Product { Sku = "B", Name = "b-updated" });

      Assert.Equal(3, list.Count);
      Assert.Equal(new[] { "A", "B", "C" }, list.Select(p => p.Sku));
      Assert.Equal("b-updated", list.First(p => p.Sku == "B").Name);
    }

    [Fact]
    public void Contains_reflects_membership_by_equality() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A", Name = "a" });

      Assert.True(list.Contains(new Product { Sku = "A" }));   // equal by Sku
      Assert.False(list.Contains(new Product { Sku = "Z" }));
    }

    [Fact]
    public void Remove_removes_the_equal_item_and_preserves_order() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A", Name = "a" });
      list.Add(new Product { Sku = "B", Name = "b" });
      list.Add(new Product { Sku = "C", Name = "c" });

      var removed = list.Remove(new Product { Sku = "B" });

      Assert.True(removed);
      Assert.Equal(new[] { "A", "C" }, list.Select(p => p.Sku));
      Assert.False(list.Contains(new Product { Sku = "B" }));
    }

    [Fact]
    public void Clear_empties_the_list() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A" });
      list.Add(new Product { Sku = "B" });

      list.Clear();

      Assert.Equal(0, list.Count);
      Assert.False(list.Contains(new Product { Sku = "A" }));
    }

    [Fact]
    public void Readd_after_remove_appends_again() {
      var list = NewList<Product>("p");
      list.Add(new Product { Sku = "A", Name = "a" });
      list.Remove(new Product { Sku = "A" });
      list.Add(new Product { Sku = "A", Name = "a2" });

      Assert.Equal(1, list.Count);
      Assert.Equal("a2", list.First().Name);
    }

    // ---------- persistence ----------

    [Fact]
    public void Save_then_reload_round_trips_items() {
      var list = NewList<Product>("persist");
      list.Add(new Product { Sku = "A", Name = "a", Price = 1m });
      list.Add(new Product { Sku = "B", Name = "b", Price = 2m });
      list.Save();

      var reloaded = NewList<Product>("persist");
      Assert.Equal(2, reloaded.Count);
      Assert.Equal(new[] { "A", "B" }, reloaded.Select(p => p.Sku));
      // Upsert must still work against the reloaded (rebuilt) index:
      reloaded.Add(new Product { Sku = "A", Name = "a-new" });
      Assert.Equal(2, reloaded.Count);
      Assert.Equal("a-new", reloaded.First(p => p.Sku == "A").Name);
    }

    // ---------- events ----------

    [Fact]
    public void Add_and_Save_fire_events() {
      var list = NewList<Product>("p");
      bool added = false, saved = false;
      list.ItemAdded += (s, e) => added = true;
      list.Saved += (s, e) => saved = true;

      list.Add(new Product { Sku = "A" });
      list.Save();

      Assert.True(added);
      Assert.True(saved);
    }
  }
}
