using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using System.Linq;
using Biggy.Extensions;
using Xunit;

namespace Biggy.Characterization {

  /// <summary>
  /// Characterization tests: they pin the CURRENT observable behavior of the
  /// reflection/mapping hot paths (ToSingle&lt;T&gt;, ToList&lt;T&gt;,
  /// RecordToExpando, ToExpandoList, ToExpando, ToDictionary) so the F1/F2/F3
  /// performance refactors can be shown to preserve behavior.
  ///
  /// These are descriptive, not prescriptive: if a test seems to assert
  /// "quirky" behavior, that is intentional - it locks in what the code does
  /// today, before any optimization.
  /// </summary>
  public class ObjectExtensionsCharacterization {

    public class Transaction {
      public int TransactionId { get; set; }
      public decimal Amount { get; set; }
      public string Comment { get; set; }
      public string Identifier { get; set; }
    }

    // ---------- ToSingle<T> ----------

    [Fact]
    public void ToSingle_maps_columns_to_properties_case_insensitively() {
      var rdr = FakeDataReader.FromRows(
        new[] { "transactionid", "AMOUNT", "Comment", "identifier" },
        new object[] { 7, 12.50m, "hello", "AA-7" });
      Assert.True(rdr.Read());

      var t = rdr.ToSingle<Transaction>();

      Assert.Equal(7, t.TransactionId);
      Assert.Equal(12.50m, t.Amount);
      Assert.Equal("hello", t.Comment);
      Assert.Equal("AA-7", t.Identifier);
    }

    [Fact]
    public void ToSingle_ignores_unmatched_columns_and_leaves_unmatched_properties_at_default() {
      var rdr = FakeDataReader.FromRows(
        new[] { "TransactionId", "NotAProperty" },
        new object[] { 42, "ignored" });
      Assert.True(rdr.Read());

      var t = rdr.ToSingle<Transaction>();

      Assert.Equal(42, t.TransactionId);
      // Columns with no matching property are silently ignored.
      // Properties with no matching column keep their default value.
      Assert.Equal(0m, t.Amount);
      Assert.Null(t.Comment);
      Assert.Null(t.Identifier);
    }

    [Fact]
    public void ToSingle_round_trips_common_types() {
      var when = new DateTime(2021, 5, 4, 3, 2, 1);
      var rdr = FakeDataReader.FromRows(
        new[] { "Id", "Ratio", "Name", "When" },
        new object[] { 99, 1.25m, "x", when });
      Assert.True(rdr.Read());

      var row = rdr.ToSingle<TypedRow>();

      Assert.Equal(99, row.Id);
      Assert.Equal(1.25m, row.Ratio);
      Assert.Equal("x", row.Name);
      Assert.Equal(when, row.When);
    }

    public class TypedRow {
      public int Id { get; set; }
      public decimal Ratio { get; set; }
      public string Name { get; set; }
      public DateTime When { get; set; }
    }

    [Fact]
    public void ToList_materializes_every_row_in_order() {
      var rdr = FakeDataReader.FromRows(
        new[] { "TransactionId", "Comment" },
        new object[] { 1, "a" },
        new object[] { 2, "b" },
        new object[] { 3, "c" });

      var list = rdr.ToList<Transaction>();

      Assert.Equal(3, list.Count);
      Assert.Equal(new[] { 1, 2, 3 }, list.Select(x => x.TransactionId));
      Assert.Equal(new[] { "a", "b", "c" }, list.Select(x => x.Comment));
    }

    // ---------- RecordToExpando / ToExpandoList ----------

    [Fact]
    public void RecordToExpando_maps_each_field_and_converts_DBNull_to_null() {
      var rdr = FakeDataReader.FromRows(
        new[] { "TransactionId", "Comment", "Identifier" },
        new object[] { 5, DBNull.Value, "AA-5" });
      Assert.True(rdr.Read());

      var d = (IDictionary<string, object>)rdr.RecordToExpando();

      Assert.Equal(3, d.Count);
      Assert.Equal(5, d["TransactionId"]);
      Assert.Null(d["Comment"]);          // DBNull -> null
      Assert.Equal("AA-5", d["Identifier"]);
    }

    [Fact]
    public void ToExpandoList_materializes_all_rows() {
      var rdr = FakeDataReader.FromRows(
        new[] { "TransactionId" },
        new object[] { 10 },
        new object[] { 20 });

      var list = rdr.ToExpandoList();

      Assert.Equal(2, list.Count);
      Assert.Equal(10, ((IDictionary<string, object>)list[0])["TransactionId"]);
      Assert.Equal(20, ((IDictionary<string, object>)list[1])["TransactionId"]);
    }

    // ---------- ToExpando / ToDictionary ----------

    [Fact]
    public void ToExpando_poco_produces_one_key_per_public_property() {
      var t = new Transaction { TransactionId = 3, Amount = 9.99m, Comment = "c", Identifier = "i" };

      var d = (IDictionary<string, object>)t.ToExpando();

      Assert.Equal(4, d.Count);
      Assert.Equal(3, d["TransactionId"]);
      Assert.Equal(9.99m, d["Amount"]);
      Assert.Equal("c", d["Comment"]);
      Assert.Equal("i", d["Identifier"]);
    }

    [Fact]
    public void ToExpando_of_an_expando_returns_the_same_instance() {
      dynamic ex = new ExpandoObject();
      ex.A = 1;

      object result = ((object)ex).ToExpando();

      Assert.Same((object)ex, result);
    }

    [Fact]
    public void ToExpando_flattens_a_NameValueCollection() {
      var nv = new NameValueCollection { { "First", "1" }, { "Second", "2" } };

      var d = (IDictionary<string, object>)nv.ToExpando();

      Assert.Equal("1", d["First"]);
      Assert.Equal("2", d["Second"]);
    }

    [Fact]
    public void ToDictionary_returns_the_property_map() {
      var t = new Transaction { TransactionId = 1, Comment = "z" };

      var d = t.ToDictionary();

      Assert.True(d.ContainsKey("TransactionId"));
      Assert.Equal(1, d["TransactionId"]);
      Assert.Equal("z", d["Comment"]);
    }
  }
}
