using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Biggy.Characterization;
using Biggy.Extensions;

namespace Biggy.Benchmarks {

  public class Row {
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Comment { get; set; }
    public string Identifier { get; set; }
  }

  internal static class Program {
    private const int RowCount = 10_000;   // matches the test workload (_qtyCrapTons)
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
    }

    private static void Build() {
      _readerRows = new List<object[]>(RowCount);
      _objects = new List<Row>(RowCount);
      for (int i = 1; i <= RowCount; i++) {
        _readerRows.Add(new object[] { i, (decimal)i, "Transaction no. " + i, "AA-" + i });
        _objects.Add(new Row { TransactionId = i, Amount = i, Comment = "Transaction no. " + i, Identifier = "AA-" + i });
      }
    }

    private static void Measure(string name, Func<int> action) {
      for (int i = 0; i < Warmup; i++) action();

      var times = new double[Iterations];
      long allocStart = GC.GetAllocatedBytesForCurrentThread();
      for (int i = 0; i < Iterations; i++) {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
      }
      long allocEnd = GC.GetAllocatedBytesForCurrentThread();

      Array.Sort(times);
      double median = times[Iterations / 2];
      double best = times[0];
      double allocPerPassMb = (allocEnd - allocStart) / (double)Iterations / (1024.0 * 1024.0);

      Console.WriteLine($"{name,-42}  median={median,7:F3} ms  best={best,7:F3} ms  alloc={allocPerPassMb,7:F2} MB/pass");
    }
  }
}
