using System;
using System.Linq;
using Xunit;

namespace Biggy.MassiveList.Characterization {

  // Contract-compliant item: Equals/GetHashCode keyed on Code (the business key),
  // with an int Id as the identity primary key the model assigns.
  public class Account {
    public int Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public override bool Equals(object obj) {
      var a = obj as Account;
      return a != null && this.Code == a.Code;
    }
    public override int GetHashCode() { return Code == null ? 0 : Code.GetHashCode(); }
  }

  public class MassiveListCharacterization {

    private static InMemoryMassiveList<Account> NewList() {
      return new InMemoryMassiveList<Account>("Id");
    }

    [Fact]
    public void Starts_empty_from_an_empty_model() {
      var list = NewList();
      Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Add_distinct_items_inserts_and_appends_in_order() {
      var list = NewList();
      list.Add(new Account { Code = "A", Name = "a" });
      list.Add(new Account { Code = "B", Name = "b" });
      list.Add(new Account { Code = "C", Name = "c" });

      Assert.Equal(3, list.Count);
      Assert.Equal(new[] { "A", "B", "C" }, list.Select(a => a.Code));
      Assert.Equal(3, list.Backing.InsertCount);          // each new item was inserted
      Assert.All(list, a => Assert.True(a.Id > 0));        // identity assigned by model
    }

    [Fact]
    public void Add_equal_item_upserts_in_place_latest_wins_no_duplicate() {
      var list = NewList();
      list.Add(new Account { Code = "X", Name = "first" });
      list.Add(new Account { Code = "X", Name = "second" });

      Assert.Equal(1, list.Count);
      Assert.Equal("second", list.First().Name);
      Assert.Equal(1, list.Backing.InsertCount);          // only the first was an insert
    }

    [Fact]
    public void Update_existing_preserves_position() {
      var list = NewList();
      list.Add(new Account { Code = "A", Name = "a" });
      list.Add(new Account { Code = "B", Name = "b" });
      list.Add(new Account { Code = "C", Name = "c" });

      list.Add(new Account { Code = "B", Name = "b2" });

      Assert.Equal(3, list.Count);
      Assert.Equal(new[] { "A", "B", "C" }, list.Select(a => a.Code));
      Assert.Equal("b2", list.First(a => a.Code == "B").Name);
    }

    [Fact]
    public void Contains_reflects_membership_by_equality() {
      var list = NewList();
      list.Add(new Account { Code = "A" });

      Assert.True(list.Contains(new Account { Code = "A" }));
      Assert.False(list.Contains(new Account { Code = "Z" }));
    }

    [Fact]
    public void Remove_removes_the_equal_item() {
      var list = NewList();
      list.Add(new Account { Code = "A" });
      list.Add(new Account { Code = "B" });

      var removed = list.Remove(new Account { Code = "A" });

      Assert.True(removed);
      Assert.Equal(1, list.Count);
      Assert.False(list.Contains(new Account { Code = "A" }));
    }

    [Fact]
    public void Clear_empties_list_and_model() {
      var list = NewList();
      list.Add(new Account { Code = "A" });
      list.Add(new Account { Code = "B" });

      list.Clear();

      Assert.Equal(0, list.Count);
      Assert.Equal(0, list.Backing.Store.Count);
      Assert.False(list.Contains(new Account { Code = "A" }));
    }

    [Fact]
    public void AddRange_bulk_inserts_and_reloads() {
      var list = NewList();
      var batch = Enumerable.Range(1, 50)
        .Select(i => new Account { Code = "C" + i, Name = "n" + i })
        .ToList();

      int affected = list.AddRange(batch);

      Assert.Equal(50, affected);
      Assert.Equal(50, list.Count);
    }

    [Fact]
    public void Reload_repopulates_from_the_model() {
      var list = NewList();
      list.Add(new Account { Code = "A" });
      list.Add(new Account { Code = "B" });

      list.Reload();

      Assert.Equal(2, list.Count);
      Assert.True(list.Contains(new Account { Code = "A" }));
    }
  }
}
