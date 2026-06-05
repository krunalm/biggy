using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Biggy;
using Biggy.Characterization;
using Biggy.Extensions;

namespace Biggy.Benchmarks {

  public class Row {
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Comment { get; set; }
    public string Identifier { get; set; }
  }

  // Contract-compliant item (Equals + GetHashCode on Sku) for the BiggyList
  // bulk-add scenario (F4).
  public class BItem {
    public string Sku { get; set; }
    public string Name { get; set; }
    public override bool Equals(object obj) {
      var p = obj as BItem;
      return p != null && this.Sku == p.Sku;
    }
    public override int GetHashCode() { return Sku == null ? 0 : Sku.GetHashCode(); }
  }

  internal static class Program {
    private const int RowCount = 10_000;   // matches the test workload (_qtyCrapTons)
    private const int BiggyCount = 5_000;  // smaller: pre-fix BiggyList.Add is O(n^2)
    private const int Iterations = 40;
    private const int Warmup = 8;

    private static readonly string[] Columns =
      { "TransactionId", "Amount", "Comment", "Identifier" };

    private static List<object[]> _readerRows;
    private static List<Row> _objects;

    private static void Main() {
      Build();

      Console.WriteLine($"Biggy mapping micro-benchmark | rows={RowCount} | iters={Iterations} | runtime={Environment.Version}");
      Console.WriteLine(new string('-', 72));

      Measure("F1 ToList<T>     (reader -> List<Row>)", () => {
        var rdr = new FakeDataReader(Columns, _readerRows);
        return rdr.ToList<Row>().Count;
      });

      Measure("F2 ToExpando     (Row -> ExpandoObject)", () => {
        int n = 0;
        foreach (var o in _objects) { var _ = o.ToExpando(); n++; }
        return n;
      });

      Measure("F3 RecordToExpando (reader -> dynamic)", () => {
        var rdr = new FakeDataReader(Columns, _readerRows);
        return rdr.ToExpandoList().Count;
      });

      // F4: bulk-add of distinct items into a fresh BiggyList. Fewer iterations
      // because the pre-fix path is O(n^2). The list is never Saved, so this
      // measures the in-memory upsert membership cost only (the temp dir keeps
      // the constructor happy and is constant overhead across before/after).
      var biggyDir = Path.Combine(Path.GetTempPath(), "biggy-bench-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(biggyDir);
      Measure($"F4 BiggyList.Add x{BiggyCount} (bulk upsert)", () => {
        var list = new BiggyList<BItem>(dbPath: biggyDir, dbName: "bench");
        for (int i = 0; i < BiggyCount; i++) {
          list.Add(new BItem { Sku = "SKU-" + i, Name = "n" + i });
        }
        return list.Count;
      }, iterations: 5, warmup: 2);
      try { Directory.Delete(biggyDir, true); } catch { }
    }

    private static void Build() {
      _readerRows = new List<object[]>(RowCount);
      _objects = new List<Row>(RowCount);
      for (int i = 1; i <= RowCount; i++) {
        _readerRows.Add(new object[] { i, (decimal)i, "Transaction no. " + i, "AA-" + i });
        _objects.Add(new Row { TransactionId = i, Amount = i, Comment = "Transaction no. " + i, Identifier = "AA-" + i });
      }
    }

    private static void Measure(string name, Func<int> action, int iterations = Iterations, int warmup = Warmup) {
      for (int i = 0; i < warmup; i++) action();

      var times = new double[iterations];
      long allocStart = GC.GetAllocatedBytesForCurrentThread();
      for (int i = 0; i < iterations; i++) {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
      }
      long allocEnd = GC.GetAllocatedBytesForCurrentThread();

      Array.Sort(times);
      double median = times[iterations / 2];
      double best = times[0];
      double allocPerPassMb = (allocEnd - allocStart) / (double)iterations / (1024.0 * 1024.0);

      Console.WriteLine($"{name,-42}  median={median,7:F3} ms  best={best,7:F3} ms  alloc={allocPerPassMb,7:F2} MB/pass");
    }
  }
}
