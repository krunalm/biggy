using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Biggy.Extensions {
  public static class ObjectExtensions {

    // Cache the reflected property set per type so the write/bulk-insert paths
    // (ToExpando -> Insert/Update/BulkInsert/BuildCommands) don't re-reflect on
    // every object. Immutable for a given Type. (Perf finding F2.)
    static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache =
      new ConcurrentDictionary<Type, PropertyInfo[]>();

    static PropertyInfo[] GetCachedProperties(Type type) {
      return _propertyCache.GetOrAdd(type, t => t.GetProperties());
    }

    // Compiled getter/setter delegates that replace the reflective PropertyInfo.
    // Get/SetValue calls (the dominant cost left after F1/F2) on the mapping hot
    // paths. They are cached PER TYPE as arrays index-aligned with the cached
    // PropertyInfo[], so the hot loops do an O(1) array index instead of a
    // per-element dictionary lookup. A null slot means "not compilable"
    // (read-only/write-only/indexed/non-public accessor) and the caller falls
    // back to reflection, preserving the original behavior exactly (including its
    // throw when a column matches a read-only property).
    // (Compiled-accessors refactor; see PERF_REFACTOR_compiled_accessors.md.)
    sealed class TypeAccessors {
      public PropertyInfo[] Props;
      public Func<object, object>[] Getters;
      public Action<object, object>[] Setters;
    }

    static readonly ConcurrentDictionary<Type, TypeAccessors> _accessorCache =
      new ConcurrentDictionary<Type, TypeAccessors>();

    static TypeAccessors GetAccessors(Type type) {
      return _accessorCache.GetOrAdd(type, t => {
        var props = GetCachedProperties(t);
        var getters = new Func<object, object>[props.Length];
        var setters = new Action<object, object>[props.Length];
        for (int i = 0; i < props.Length; i++) {
          getters[i] = BuildGetter(props[i]);
          setters[i] = BuildSetter(props[i]);
        }
        return new TypeAccessors { Props = props, Getters = getters, Setters = setters };
      });
    }

    static Func<object, object> BuildGetter(PropertyInfo p) {
      // Only public, parameterless getters get a compiled delegate; anything
      // else returns null so the caller falls back to reflection.
      if (p.GetIndexParameters().Length != 0 || p.GetGetMethod(false) == null) return null;
      var o = Expression.Parameter(typeof(object), "o");
      var body = Expression.Convert(
        Expression.Property(Expression.Convert(o, p.DeclaringType), p),
        typeof(object));
      return Expression.Lambda<Func<object, object>>(body, o).Compile();
    }

    static Action<object, object> BuildSetter(PropertyInfo p) {
      // Only public, parameterless setters get a compiled delegate; anything
      // else returns null so the caller falls back to reflection (which can write
      // non-public members and preserves the original throw on read-only props).
      if (p.GetIndexParameters().Length != 0 || p.GetSetMethod(false) == null) return null;
      var o = Expression.Parameter(typeof(object), "o");
      var v = Expression.Parameter(typeof(object), "v");
      var assign = Expression.Assign(
        Expression.Property(Expression.Convert(o, p.DeclaringType), p),
        Expression.Convert(v, p.PropertyType));
      return Expression.Lambda<Action<object, object>>(assign, o, v).Compile();
    }


    public static void CloneFromObject(this object o, object record) {
      var props = o.GetType().GetProperties();
      var dictionary = record.ToDictionary();
      foreach (var prop in props) {
        var propName = prop.Name;
        foreach (var key in dictionary.Keys) {
          if (key.Equals(propName, StringComparison.InvariantCultureIgnoreCase)) {
            prop.SetValue(o, dictionary[key]);
          }
        }
      }
    }

    /// <summary>
    /// Extension method for adding in a bunch of parameters
    /// </summary>
    public static void AddParams(this DbCommand cmd, params object[] args) {
      foreach (var item in args) {
        AddParam(cmd, item);
      }
    }

    /// <summary>
    /// Extension for adding single parameter
    /// </summary>
    public static void AddParam(this DbCommand cmd, object item) {
      var p = cmd.CreateParameter();
      p.ParameterName = string.Format("@{0}", cmd.Parameters.Count);
      if (item == null) {
        p.Value = DBNull.Value;
      } else {
        if (item.GetType() == typeof(Guid)) {
          p.Value = item.ToString();
          p.DbType = DbType.String;
          p.Size = 4000;
        } else if (item.GetType() == typeof(ExpandoObject)) {
          var d = (IDictionary<string, object>)item;
          p.Value = d.Values.FirstOrDefault();
        } else {
          p.Value = item;
        }
        if (item.GetType() == typeof(string)) {
          p.Size = ((string)item).Length > 4000 ? -1 : 4000;
        }
      }
      cmd.Parameters.Add(p);
    }

    /// <summary>
    /// Turns an IDataReader to a Dynamic list of things
    /// </summary>
    public static List<dynamic> ToExpandoList(this IDataReader rdr) {
      var result = new List<dynamic>();
      while (rdr.Read()) {
        result.Add(rdr.RecordToExpando());
      }
      return result;
    }

    public static dynamic RecordToExpando(this IDataReader rdr) {
      dynamic e = new ExpandoObject();
      var d = e as IDictionary<string, object>;
      for (int i = 0; i < rdr.FieldCount; i++) {
        d.Add(rdr.GetName(i), DBNull.Value.Equals(rdr[i]) ? null : rdr[i]);
      }
      return e;
    }

    public static List<T> ToList<T>(this IDataReader rdr) where T : new() {
      var result = new List<T>();
      while (rdr.Read()) {
        result.Add(rdr.ToSingle<T>());
      }
      return result;
    }

    public static T ToSingle<T>(this IDataReader rdr) where T : new() {
      var item = new T();
      // typeof(T) == item.GetType() under the new() constraint. Cached, type-
      // aligned property + compiled-setter arrays remove per-row reflection (F1)
      // and per-cell reflective SetValue (compiled accessors).
      var acc = GetAccessors(typeof(T));
      var props = acc.Props;
      var setters = acc.Setters;
      for (int p = 0; p < props.Length; p++) {
        for (int i = 0; i < rdr.FieldCount; i++) {
          if (rdr.GetName(i).Equals(props[p].Name, StringComparison.InvariantCultureIgnoreCase)) {
            var val = rdr.GetValue(i);
            var setter = setters[p];
            if (setter != null) {
              setter(item, val);
            } else {
              props[p].SetValue(item, val);
            }
          }
        }
      }
      return item;
    }

    /// <summary>
    /// Turns the object into an ExpandoObject
    /// </summary>
    public static dynamic ToExpando(this object o) {
      var result = new ExpandoObject();
      var d = result as IDictionary<string, object>; //work with the Expando as a Dictionary
      if (o.GetType() == typeof(ExpandoObject)) return o; //shouldn't have to... but just in case
      if (o.GetType() == typeof(NameValueCollection) || o.GetType().IsSubclassOf(typeof(NameValueCollection))) {
        var nv = (NameValueCollection)o;
        nv.Cast<string>().Select(key => new KeyValuePair<string, object>(key, nv[key])).ToList().ForEach(i => d.Add(i));
      } else {
        var acc = GetAccessors(o.GetType());
        var props = acc.Props;
        var getters = acc.Getters;
        for (int i = 0; i < props.Length; i++) {
          var getter = getters[i];
          d.Add(props[i].Name, getter != null ? getter(o) : props[i].GetValue(o, null));
        }
      }
      return result;
    }

    /// <summary>
    /// Turns the object into a Dictionary
    /// </summary>
    public static IDictionary<string, object> ToDictionary(this object thingy) {
      return (IDictionary<string, object>)thingy.ToExpando();
    }
  }


}
