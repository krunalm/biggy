using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Biggy.Massive;

namespace Biggy {
  public class MassiveList<T> : ICollection<T> where T : new(){

    List<T> _items = null;
    // Membership index kept in sync with _items so Add/Contains are amortized
    // O(1) instead of O(n) (perf finding F7 - bulk Add was O(n^2)). Relies on T
    // implementing GetHashCode consistently with Equals (the same Equals that
    // already drives the upsert semantics below).
    HashSet<T> _index = null;
    public string DbDirectory { get; set; }
    public bool InMemory { get; set; }
    public string TableName { get; set; }
    public string ConnectionString { get; set; }
    public DBTable Model { get; set; }

    public event EventHandler ItemRemoved;
    public event EventHandler ItemAdded;
    public event EventHandler Changed;
    public event EventHandler Loaded;


    public MassiveList(string connectionStringName, string tableName = "guess", string primaryKeyName = "id") {
      // Null-safe so the model can be substituted (e.g. in tests) without a
      // configured connection string; a real model still fails at connect time.
      var connSetting = ConfigurationManager.ConnectionStrings[connectionStringName];
      this.ConnectionString = connSetting == null ? null : connSetting.ConnectionString;
      if (tableName!="guess") {
        this.TableName = tableName;
      } else {
        var thingyType = this.GetType().GenericTypeArguments[0].Name;
        this.TableName = Inflector.Inflector.Pluralize(thingyType).ToLower();
      }
      this.Model = CreateModel(connectionStringName, this.TableName, primaryKeyName);
      this.Reload();

      if (this.Loaded != null) {
        var args = new BiggyEventArgs<T>();
        args.Items = _items;
        this.Loaded.Invoke(this, args);
      }
    }

    // Seam for substituting the data model (e.g. an in-memory DBTable in tests).
    // Default behavior is unchanged: a real DBTable bound to the connection.
    protected virtual DBTable CreateModel(string connectionStringName, string tableName, string primaryKeyName) {
      return new DBTable(connectionStringName, tableName, primaryKeyName);
    }

    public IEnumerable<T> Query(string sql, params object[] args) {
      return this.Model.Query<T>(sql, args);
    }

    public void Purge() {
      this.Clear();
    }

    public void Reload() {
      _items = this.Model.All<T>().ToList();
      RebuildIndex();
    }

    // Rebuilds the membership index from the current _items (after load/reload).
    void RebuildIndex() {
      _index = _items == null ? new HashSet<T>() : new HashSet<T>(_items);
    }

    public void Update(T item) {
      var index = _items.IndexOf(item);
      if (index > -1) {
        this.Model.Update(item, this.Model.GetPrimaryKey(item));
        _items.RemoveAt(index);
        _items.Insert(index, item);
        // Swap the stored reference in the index for the new (equal) item.
        _index.Remove(item);
        _index.Add(item);
      } else {
        this.Model.Insert(item);
        Add(item);
      }
    }

    public void Add(T item) {
      if (_index.Contains(item)) {
        //let's not overwrite -- this will be determined by
        //item.Equals()
        this.Update(item);
      } else {
        this.Model.Insert(item);
        _items.Add(item);
        _index.Add(item);
      }
      if (this.ItemAdded != null) {
        var args = new BiggyEventArgs<T>();
        args.Item = item;
        this.ItemAdded.Invoke(this, args);
      }
      if (this.Changed != null) {
        var args = new BiggyEventArgs<T>();
        args.Item = item;
        this.Changed.Invoke(this, args);
      }
    }

    public int AddRange(List<T> items) {
        int affected = this.Model.BulkInsert(items);
        this.Reload();
        return affected;
    }
      
    public void Clear() {
      _items.Clear();
      _index.Clear();
      this.Model.DeleteWhere("");
      if (this.Changed != null) {
        var args = new BiggyEventArgs<T>();
        this.Changed.Invoke(this, args);
      }
    }

    public bool Contains(T item) {
      return _index.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex) {
      _items.CopyTo(array, arrayIndex);
    }

    public int Count {
      get { return _items.Count; }
    }

    public bool IsReadOnly {
      get { return false; }
    }

    public bool Remove(T item) {
      this.Model.Delete(this.Model.GetPrimaryKey(item));
      if (this.ItemRemoved != null) {
        var args = new BiggyEventArgs<T>();
        args.Item = item;
        this.ItemRemoved.Invoke(this, args);
      }
      if (this.Changed != null) {
        var args = new BiggyEventArgs<T>();
        args.Item = item;
        this.Changed.Invoke(this, args);
      }
      _index.Remove(item);
      return _items.Remove(item);
    }

    public IEnumerator<T> GetEnumerator() {
      return _items.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
      return _items.GetEnumerator();
    }
  }
}
