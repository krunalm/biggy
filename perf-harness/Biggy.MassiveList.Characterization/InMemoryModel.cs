using System;
using System.Collections.Generic;
using System.Linq;
using Biggy;
using Biggy.Massive;

namespace Biggy.MassiveList.Characterization {

  // An in-memory DBTable that backs MassiveList without any database. It overrides
  // every data method MassiveList calls (All/Insert/Update/Delete/BulkInsert/
  // DeleteWhere) so the real Massive ADO/SQL code is never reached. This lets the
  // real MassiveList<T> logic (the part F7 changes) run and be asserted.
  public class InMemoryDBTable : DBTable {
    public readonly List<object> Store = new List<object>();
    public int InsertCount = 0;
    public int ReloadCount = 0;
    private int _counter = 0;

    public InMemoryDBTable(string primaryKeyName) : base("__none__", "things", primaryKeyName) { }

    public override IEnumerable<TT> All<TT>(string where = "", string orderBy = "", int limit = 0, string columns = "*", params object[] args) {
      ReloadCount++;
      return Store.Cast<TT>().ToList();
    }

    public override dynamic Insert(object o) {
      AssignPk(o);
      Store.Add(o);
      InsertCount++;
      return o;
    }

    public override int Update(object o, object key) {
      for (int i = 0; i < Store.Count; i++) {
        if (object.Equals(GetPrimaryKey(Store[i]), key)) { Store[i] = o; return 1; }
      }
      return 0;
    }

    public override int BulkInsert<TT>(List<TT> items) {
      foreach (var it in items) { AssignPk(it); Store.Add(it); }
      InsertCount += items.Count;
      return items.Count;
    }

    public override int Delete(object key) {
      for (int i = 0; i < Store.Count; i++) {
        if (object.Equals(GetPrimaryKey(Store[i]), key)) { Store.RemoveAt(i); return 1; }
      }
      return 0;
    }

    public override int DeleteWhere(string where = "", params object[] args) {
      var n = Store.Count;
      Store.Clear();
      return n;
    }

    private void AssignPk(object o) {
      var prop = o.GetType().GetProperty(PrimaryKeyField);
      if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int)) {
        prop.SetValue(o, ++_counter);
      }
    }
  }

  // MassiveList wired to an in-memory model via the CreateModel seam.
  public class InMemoryMassiveList<T> : Biggy.MassiveList<T> where T : new() {
    public InMemoryDBTable Backing;

    public InMemoryMassiveList(string primaryKeyName)
      : base("__none__", "things", primaryKeyName) { }

    protected override DBTable CreateModel(string connectionStringName, string tableName, string primaryKeyName) {
      Backing = new InMemoryDBTable(primaryKeyName);
      return Backing;
    }
  }
}
