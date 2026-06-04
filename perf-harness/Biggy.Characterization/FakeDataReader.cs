using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Biggy.Characterization {

  /// <summary>
  /// A minimal in-memory <see cref="IDataReader"/> used to drive the Biggy
  /// mapping extensions (ToSingle&lt;T&gt;, ToList&lt;T&gt;, RecordToExpando,
  /// ToExpandoList) without a real database connection.
  ///
  /// Only the members actually exercised by ObjectExtensions are implemented;
  /// everything else throws so that any accidental new dependency surfaces
  /// loudly instead of silently returning bogus data.
  /// </summary>
  public sealed class FakeDataReader : IDataReader {
    private readonly string[] _columns;
    private readonly IList<object[]> _rows;
    private int _index = -1;

    public FakeDataReader(string[] columns, IList<object[]> rows) {
      _columns = columns;
      _rows = rows;
    }

    /// <summary>Convenience builder from a list of ordered column/value maps.</summary>
    public static FakeDataReader FromRows(string[] columns, params object[][] rows) {
      return new FakeDataReader(columns, rows.ToList());
    }

    private object[] Current => _rows[_index];

    // --- members used by ObjectExtensions ---
    public bool Read() {
      _index++;
      return _index < _rows.Count;
    }

    public int FieldCount => _columns.Length;
    public string GetName(int i) => _columns[i];
    public object GetValue(int i) => Current[i];
    public object this[int i] => Current[i];

    // --- unused members: fail fast if the code under test starts relying on them ---
    public object this[string name] => throw new NotSupportedException();
    public int Depth => 0;
    public bool IsClosed => false;
    public int RecordsAffected => 0;
    public void Close() { }
    public void Dispose() { }
    public bool NextResult() => false;
    public int GetOrdinal(string name) => Array.FindIndex(_columns, c => c == name);
    public string GetDataTypeName(int i) => throw new NotSupportedException();
    public Type GetFieldType(int i) => Current[i]?.GetType() ?? typeof(object);
    public bool IsDBNull(int i) => Current[i] == null || Convert.IsDBNull(Current[i]);
    public DataTable GetSchemaTable() => throw new NotSupportedException();
    public bool GetBoolean(int i) => (bool)Current[i];
    public byte GetByte(int i) => (byte)Current[i];
    public long GetBytes(int i, long fo, byte[] buf, int bo, int len) => throw new NotSupportedException();
    public char GetChar(int i) => (char)Current[i];
    public long GetChars(int i, long fo, char[] buf, int bo, int len) => throw new NotSupportedException();
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public DateTime GetDateTime(int i) => (DateTime)Current[i];
    public decimal GetDecimal(int i) => (decimal)Current[i];
    public double GetDouble(int i) => (double)Current[i];
    public float GetFloat(int i) => (float)Current[i];
    public Guid GetGuid(int i) => (Guid)Current[i];
    public short GetInt16(int i) => (short)Current[i];
    public int GetInt32(int i) => (int)Current[i];
    public long GetInt64(int i) => (long)Current[i];
    public string GetString(int i) => (string)Current[i];
    public int GetValues(object[] values) { Array.Copy(Current, values, Current.Length); return Current.Length; }
  }
}
