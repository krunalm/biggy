using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Biggy.Extensions;

namespace Biggy.Massive
{

  /// <summary>
  /// A class that wraps your database table in Dynamic Funtime
  /// </summary>
  public class DBTable : DynamicObject {
    
    protected string ConnectionString;

    public static DBTable Open(string connectionStringName) {
      dynamic dm = new DBTable(connectionStringName);
      return dm;
    }

    public DBTable(string connectionStringName, string tableName = "",
      string primaryKeyField = "", string descriptorField = "", bool pkIsIdentityColumn = true)
    {
      TableName = tableName == "" ? this.GetType().Name : tableName;
      PrimaryKeyField = string.IsNullOrEmpty(primaryKeyField) ? "ID" : primaryKeyField;
      DescriptorField = descriptorField;
      PkIsIdentityColumn = pkIsIdentityColumn;
      ConnectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
    }

    /// <summary>
    /// Conventionally introspects the object passed in for a field that 
    /// looks like a PK. If you've named your PrimaryKeyField, this becomes easy
    /// </summary>
    public virtual bool HasPrimaryKey(object o)
    {
      return o.ToDictionary().ContainsKey(PrimaryKeyField);
    }

    /// <summary>
    /// If the object passed in has a property with the same name as your PrimaryKeyField
    /// it is returned here.
    /// </summary>
    public virtual object GetPrimaryKey(object o)
    {
      object result = null;
      o.ToDictionary().TryGetValue(PrimaryKeyField, out result);
      return result;
    }

    public virtual string PrimaryKeyField { get; set; }
    public virtual bool PkIsIdentityColumn { get; set; }
    public virtual string TableName { get; set; }
    public string DescriptorField { get; protected set; }



    /// <summary>
    /// Enumerates the reader yielding the result - thanks to Jeroen Haegebaert
    /// </summary>
    public virtual IEnumerable<T> Query<T>(string sql, params object[] args) where T : new() {
      using (var conn = OpenConnection()) {
        var rdr = CreateCommand(sql, conn, args).ExecuteReader();
        while (rdr.Read()) {
          yield return rdr.ToSingle<T>();
        }
      }
    }

    public virtual IEnumerable<T> Query<T>(string sql, DbConnection connection, params object[] args) where T : new() {
      using (var rdr = CreateCommand(sql, connection, args).ExecuteReader()) {
        while (rdr.Read()) {
          yield return rdr.ToSingle<T>();
        }
      }
    }

    /// <summary>
    /// Enumerates the reader yielding the result - thanks to Jeroen Haegebaert
    /// </summary>
    public virtual IEnumerable<dynamic> Query(string sql, params object[] args) {
      using (var conn = OpenConnection()) {
        var rdr = CreateCommand(sql, conn, args).ExecuteReader();
        while (rdr.Read()) {
            yield return rdr.RecordToExpando(); ;
        }
      }
    }

    public virtual IEnumerable<dynamic> Query(string sql, DbConnection connection, params object[] args) {
      using (var rdr = CreateCommand(sql, connection, args).ExecuteReader()) {
        while (rdr.Read()) {
            yield return rdr.RecordToExpando(); ;
        }
      }
    }

    /// <summary>
    /// Returns a single result
    /// </summary>
    public virtual object Scalar(string sql, params object[] args) {
      object result = null;
      using (var conn = OpenConnection()) {
        result = CreateCommand(sql, conn, args).ExecuteScalar();
      }
      return result;
    }

    /// <summary>
    /// Creates a DBCommand that you can use for loving your database.
    /// </summary>
    public virtual DbCommand CreateCommand(string sql, DbConnection conn, params object[] args) {
      var result = conn.CreateCommand();
      result.Connection = conn;
      result.CommandText = sql;
      if (args.Length > 0) {
        result.AddParams(args);
      }
      return result;
    }

    /// <summary>
    /// Returns and OpenConnection
    /// </summary>
    public virtual DbConnection OpenConnection() {
      //hard-code this, the overrides for PG etc will reset this.
      var result = new SqlConnection(this.ConnectionString);
      result.Open();
      return result;
    }

    /// <summary>
    /// Builds a set of Insert and Update commands based on the passed-on objects.
    /// These objects can be POCOs, Anonymous, NameValueCollections, or Expandos. Objects
    /// With a PK property (whatever PrimaryKeyField is set to) will be created at UPDATEs
    /// </summary>
    public virtual List<DbCommand> BuildCommands(params object[] things) {
      var commands = new List<DbCommand>();
      foreach (var item in things) {
        if (HasPrimaryKey(item)) {
          commands.Add(CreateUpdateCommand(item.ToExpando(), GetPrimaryKey(item)));
        } else {
          commands.Add(CreateInsertCommand(item.ToExpando()));
        }
      }
      return commands;
    }

    public virtual int Execute(DbCommand command) {
      return Execute(new DbCommand[] { command });
    }

    public virtual int Execute(string sql, params object[] args) {
      return Execute(CreateCommand(sql, null, args));
    }

    /// <summary>
    /// Executes a series of DBCommands in a transaction
    /// </summary>
    public virtual int Execute(IEnumerable<DbCommand> commands) {
      var result = 0;
      using (var conn = OpenConnection()) {
        using (var tx = conn.BeginTransaction()) {
          foreach (var cmd in commands) {
            cmd.Connection = conn;
            cmd.Transaction = tx;
            result += cmd.ExecuteNonQuery();
          }
          tx.Commit();
        }
      }
      return result;
    }

    /// <summary>
    /// Returns all records complying with the passed-in WHERE clause and arguments, 
    /// ordered as specified, limited (TOP) by limit.
    /// </summary>
    public virtual IEnumerable<dynamic> All(string where = "", string orderBy = "", int limit = 0, string columns = "*", params object[] args) {
      string sql = BuildSelect(where, orderBy, limit);
      return Query(string.Format(sql, columns, TableName), args);
    }

    public virtual IEnumerable<T> All<T>(string where = "", string orderBy = "", int limit = 0, string columns = "*", params object[] args) where T : new() {
      string sql = BuildSelect(where, orderBy, limit);
      return Query<T>(string.Format(sql, columns, TableName), args);
    }

    protected virtual string BuildSelect(string where, string orderBy, int limit) {
      string sql = limit > 0 ? "SELECT TOP " + limit + " {0} FROM {1} " : "SELECT {0} FROM {1} ";
      if (!string.IsNullOrEmpty(where)) {
        sql += where.Trim().StartsWith("where", StringComparison.OrdinalIgnoreCase) ? where : " WHERE " + where;
      }
      if (!String.IsNullOrEmpty(orderBy)) {
        sql += orderBy.Trim().StartsWith("order by", StringComparison.OrdinalIgnoreCase) ? orderBy : " ORDER BY " + orderBy;
      }
      return sql;
    }

    /// <summary>
    /// Returns a single row from the database
    /// </summary>
    public virtual T FirstOrDefault<T>(string where, params object[] args) where T: new() {
      var result = new T();
      var sql = string.Format("SELECT TOP 2 * FROM {0} WHERE {1}", TableName, where);
      return Query<T>(sql, args).FirstOrDefault();
    }

    /// <summary>
    /// Returns a single row from the database
    /// </summary>
    public virtual T Find<T>(object key) where T : new() {
      var result = new T();
      var sql = string.Format("SELECT TOP 2 * FROM {0} WHERE {1} = @0", TableName, PrimaryKeyField);
      return Query<T>(sql, key).FirstOrDefault();
    }

    /// <summary>
    /// Returns a single row from the database
    /// </summary>
    public virtual dynamic FirstOrDefault(string where, params object[] args) {
      var sql = string.Format("SELECT TOP 2 * FROM {0} WHERE {1}", TableName, where);
      return Query(sql, args).FirstOrDefault();
    }

    /// <summary>
    /// Returns a single row from the database
    /// </summary>
    public virtual dynamic Find(object key) {
      var sql = string.Format("SELECT TOP 2 * FROM {0} WHERE {1} = @0", TableName, PrimaryKeyField);
      return Query(sql, key).FirstOrDefault();
    }

    /// <summary>
    /// This will return an Expando as a Dictionary
    /// </summary>
    public virtual IDictionary<string, object> ItemAsDictionary(ExpandoObject item){
      return (IDictionary<string, object>)item;
    }

    //Checks to see if a key is present based on the passed-in value
    public virtual bool ItemContainsKey(string key, ExpandoObject item) {
      var dc = ItemAsDictionary(item);
      return dc.ContainsKey(key);
    }

    /// <summary>
    /// Executes a set of objects as Insert or Update commands based on their property settings, within a transaction.
    /// These objects can be POCOs, Anonymous, NameValueCollections, or Expandos. Objects
    /// With a PK property (whatever PrimaryKeyField is set to) will be created at UPDATEs
    /// </summary>
    public virtual int Save(params object[] things) {
      foreach (var item in things) {
        if (!IsValid(item)) {
          throw new InvalidOperationException("Can't save this item: " + String.Join("; ", Errors.ToArray()));
        }
      }
      var commands = BuildCommands(things);
      return Execute(commands);
    }

    public virtual DbCommand CreateInsertCommand(dynamic expando) {
      DbCommand result = null;
      var settings = (IDictionary<string, object>)expando;
      var sbKeys = new StringBuilder();
      var sbVals = new StringBuilder();
      var stub = "INSERT INTO {0} ({1}) \r\n VALUES ({2})";
      result = CreateCommand(stub, null);
      int counter = 0;
      if (PkIsIdentityColumn) {
        settings.Remove(PrimaryKeyField);
      }
      foreach (var item in settings) {
        sbKeys.AppendFormat("{0},", item.Key);
        sbVals.AppendFormat("@{0},", counter.ToString());
        result.AddParam(item.Value);
        counter++;
      }
      if (counter > 0) {
        var keys = sbKeys.ToString().Substring(0, sbKeys.Length - 1);
        var vals = sbVals.ToString().Substring(0, sbVals.Length - 1);
        var sql = string.Format(stub, TableName, keys, vals);
        result.CommandText = sql;
      }
      else throw new InvalidOperationException("Can't parse this object to the database - there are no properties set");
      return result;
    }

    public virtual List<DbCommand> CreateInsertBatchCommands<T>(List<T> newRecords) {
      // The magic SQL Server Parameter Limit:
      var MAGIC_PARAMETER_LIMIT = 2100;
      var MAGIC_ROW_VALUE_LIMIT = 1000;
      int paramCounter = 0;
      int rowValueCounter = 0;
      var commands = new List<DbCommand>();

      // We need a sample to grab the object schema:
      var first = newRecords.First().ToExpando();
      var schema = (IDictionary<string, object>)first;

      // Remove identity column - "can't touch this..."
      if (PkIsIdentityColumn) {
        schema.Remove(PrimaryKeyField);
      }

      var sbFieldNames = new StringBuilder();
      foreach (var field in schema) {
        sbFieldNames.AppendFormat("{0},", field.Key);
      }
      var keys = sbFieldNames.ToString().Substring(0, sbFieldNames.Length - 1);

      // Get the core of the INSERT statement, then append each set of field params per record:
      var sqlStub = string.Format("INSERT INTO {0} ({1}) VALUES ", TableName, keys);
      var sbSql = new StringBuilder(sqlStub);
      var dbCommand = CreateCommand("", null);

      foreach (var item in newRecords) {
        // Things explode if you exceed the param limit for SQL Server:
        if (paramCounter + schema.Count >= MAGIC_PARAMETER_LIMIT || rowValueCounter >= MAGIC_ROW_VALUE_LIMIT) {
          // Add the current command to the list, then start over with another:
          dbCommand.CommandText = sbSql.ToString().Substring(0, sbSql.Length - 1);
          commands.Add(dbCommand);
          sbSql = new StringBuilder(sqlStub);
          paramCounter = 0;
          rowValueCounter = 0;
          dbCommand = CreateCommand("", null);
        }
        var ex = item.ToExpando();

        // Can't insert against an Identity field:
        var itemSchema = (IDictionary<string, object>)ex;
        if (PkIsIdentityColumn) {
          itemSchema.Remove(PrimaryKeyField);
        }
        var sbParamGroup = new StringBuilder();
        foreach (var fieldValue in itemSchema.Values) {
          sbParamGroup.AppendFormat("@{0},", paramCounter.ToString());
          dbCommand.AddParam(fieldValue);
          paramCounter++;
        }
        // Make a whole record to insert (we are inserting like this - (@0,@1,@2), (@3,@4,@5), (etc, etc, etc) . . .
        sbSql.AppendFormat("({0}),", sbParamGroup.ToString().Substring(0, sbParamGroup.Length - 1));
        rowValueCounter++;
      }
      dbCommand.CommandText = sbSql.ToString().Substring(0, sbSql.Length - 1);
      commands.Add(dbCommand);
      return commands;
    }

    /// <summary>
    /// Creates a command for use with transactions - internal stuff mostly, but here for you to play with
    /// </summary>
    public virtual DbCommand CreateUpdateCommand(dynamic expando, object key) {
      var settings = (IDictionary<string, object>)expando;
      var sbKeys = new StringBuilder();
      var stub = "UPDATE {0} SET {1} WHERE {2} = @{3}";
      var args = new List<object>();
      var result = CreateCommand(stub, null);
      int counter = 0;
      foreach (var item in settings) {
        var val = item.Value;
        if (!item.Key.Equals(PrimaryKeyField, StringComparison.OrdinalIgnoreCase) && item.Value != null) {
          result.AddParam(val);
          sbKeys.AppendFormat("{0} = @{1}, \r\n", item.Key, counter.ToString());
          counter++;
        }
      }
      if (counter > 0)
      {
        //add the key
        result.AddParam(key);
        //strip the last commas
        var keys = sbKeys.ToString().Substring(0, sbKeys.Length - 4);
        result.CommandText = string.Format(stub, TableName, keys, PrimaryKeyField, counter);
      } else {
        throw new InvalidOperationException("No parsable object was sent in - could not divine any name/value pairs");
      }
      return result;
    }

    /// <summary>
    /// Removes one or more records from the DB according to the passed-in WHERE
    /// </summary>
    public virtual DbCommand CreateDeleteCommand(string where = "", object key = null, params object[] args) {
      var sql = string.Format("DELETE FROM {0} ", TableName);
      if (key != null) {
        sql += string.Format("WHERE {0}=@0", PrimaryKeyField);
        args = new object[] { key };
      }
      else if (!string.IsNullOrEmpty(where)) {
        sql += where.Trim().StartsWith("where", StringComparison.OrdinalIgnoreCase) ? where : "WHERE " + where;
      }
      return CreateCommand(sql, null, args);
    }

    public bool IsValid(dynamic item) {
      Errors.Clear();
      Validate(item);
      return Errors.Count == 0;
    }

    //Temporary holder for error messages
    public IList<string> Errors = new List<string>();

    /// <summary>
    /// Adds a record to the database. You can pass in an Anonymous object, an ExpandoObject,
    /// A regular old POCO, or a NameValueColletion from a Request.Form or Request.QueryString
    /// </summary>
    public virtual dynamic Insert(object o) {
      var ex = o.ToExpando();
      if (!IsValid(ex)) {
        throw new InvalidOperationException("Can't insert: " + String.Join("; ", Errors.ToArray()));
      }
      if (BeforeSave(ex)) {
        using (dynamic conn = OpenConnection()) {
          var cmd = CreateInsertCommand(ex);
          cmd.Connection = conn;
          cmd.ExecuteNonQuery();
          if (PkIsIdentityColumn)
          {
            cmd.CommandText = "SELECT SCOPE_IDENTITY() as newID";
            // Work with expando as dictionary:
            var d = ex as IDictionary<string, object>;
            // Set the new identity/PK:
            d[PrimaryKeyField] = (int)cmd.ExecuteScalar();

            // If a non-anonymous type was passed, see if we can just assign
            // the new ID to the reference originally passed in:
            var props = o.GetType().GetProperties();
            if(props.Any(p => p.Name == PrimaryKeyField))
            {
              var idField = props.First(p => p.Name == PrimaryKeyField);
              idField.SetValue(o, d[PrimaryKeyField]);
            }
          }
          Inserted(ex);
        }
        return ex;
      } else {
        return null;
      }
    }

    /// <summary>
    /// Inserts a large range - does not check for existing entires, and assumes all 
    /// included records are new records. Order of magnitude more performant than standard
    /// Insert method for multiple sequential inserts. 
    /// </summary>
    /// <param name="items"></param>
    /// <returns></returns>
    public virtual int BulkInsert<T>(List<T> items) {
      var first = items.First();
      var ex = first.ToExpando();
      var itemSchema = (IDictionary<string, object>)ex;
      var itemParameterCount = itemSchema.Values.Count();
      var requiredParams = items.Count * itemParameterCount;
      var batchCounter = requiredParams / 2000;

      var rowsAffected = 0;
      if (items.Count() > 0) {
        using (dynamic conn = OpenConnection()) {
          var commands = CreateInsertBatchCommands(items);
          foreach (var cmd in commands) {
            cmd.Connection = conn;
            rowsAffected += cmd.ExecuteNonQuery();
          }
        }
      }
      return rowsAffected;
    }

    /// <summary>
    /// Updates a record in the database. You can pass in an Anonymous object, an ExpandoObject,
    /// A regular old POCO, or a NameValueCollection from a Request.Form or Request.QueryString
    /// </summary>
    public virtual int Update(object o, object key) {
      var ex = o.ToExpando();
      if (!IsValid(ex)) {
        throw new InvalidOperationException("Can't Update: " + String.Join("; ", Errors.ToArray()));
      }
      var result = 0;
      if (BeforeSave(ex)) {
        result = Execute(CreateUpdateCommand(ex, key));
        Updated(ex);
      }
      return result;
    }


    public virtual int Update<T>(T o)
    {
      var ex = o.ToExpando();
      var d = (IDictionary<string, object>)ex;
      if (HasPrimaryKey(o))
      {
        var pkValue = d[this.PrimaryKeyField];
        return this.Update(o, pkValue);
      }
      else
      {
        throw new InvalidOperationException("No Pirmary Key Specified - Can't parse unique record to update");
      }
    }

    /// <summary>
    /// Removes one or more records from the DB according to the passed-in WHERE
    /// </summary>
    public int Delete(object key) {
      var deleted = this.Find(key);
      var result = 0;
      if (BeforeDelete(deleted)) {
        result = Execute(CreateDeleteCommand(key: key));
        Deleted(deleted);
      }
      return result;
    }

    /// <summary>
    /// Removes one or more records from the DB according to the passed-in WHERE
    /// </summary>
    public int DeleteWhere(string where = "", params object[] args) {
      return Execute(CreateDeleteCommand(where: where, args: args));
    }

    public void DefaultTo(string key, object value, dynamic item) {
      if (!ItemContainsKey(key, item)) {
        var dc = (IDictionary<string, object>)item;
        dc[key] = value;
      }
    }

    //Hooks
    public virtual void Validate(dynamic item) { }
    public virtual void Inserted(dynamic item) { }
    public virtual void Updated(dynamic item) { }
    public virtual void Deleted(dynamic item) { }
    public virtual bool BeforeDelete(dynamic item) { return true; }
    public virtual bool BeforeSave(dynamic item) { return true; }

    //validation methods
    public virtual void ValidatesPresenceOf(object value, string message = "Required") {
      if (value == null) {
        Errors.Add(message);
      }
      if (String.IsNullOrEmpty(value.ToString())) {
        Errors.Add(message);
      }
    }

    //fun methods
    public virtual void ValidatesNumericalityOf(object value, string message = "Should be a number") {
      var type = value.GetType().Name;
      var numerics = new string[] { "Int32", "Int16", "Int64", "Decimal", "Double", "Single", "Float" };
      if (!numerics.Contains(type)) {
        Errors.Add(message);
      }
    }

    public virtual void ValidateIsCurrency(object value, string message = "Should be money") {
      if (value == null) {
        Errors.Add(message);
      }
      decimal val = decimal.MinValue;
      decimal.TryParse(value.ToString(), out val);
      if (val == decimal.MinValue) {
        Errors.Add(message);
      }
    }

    public int Count() {
      return Count(TableName);
    }

    public int Count(string tableName, string where = "", params object[] args) {
      return (int)Scalar("SELECT COUNT(*) FROM " + tableName + " " + where, args);
    }

    /// <summary>
    /// A helpful query tool
    /// </summary>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) {
      //parse the method
      var constraints = new List<string>();
      var counter = 0;
      var info = binder.CallInfo;
      // accepting named args only... SKEET!
      if (info.ArgumentNames.Count != args.Length) {
          throw new InvalidOperationException("Please use named arguments for this type of query - the column name, orderby, columns, etc");
      }
      //first should be "FindBy, Last, Single, First"
      var op = binder.Name;
      var columns = " * ";
      string orderBy = string.Format(" ORDER BY {0}", PrimaryKeyField);
      string sql = "";
      string where = "";
      var whereArgs = new List<object>();

      //loop the named args - see if we have order, columns and constraints
      if (info.ArgumentNames.Count > 0) {
        for (int i = 0; i < args.Length; i++) {
          var name = info.ArgumentNames[i].ToLower();
          switch (name)
          {
              case "orderby":
                  orderBy = " ORDER BY " + args[i];
                  break;
              case "columns":
                  columns = args[i].ToString();
                  break;
              default:
                  constraints.Add(string.Format(" {0} = @{1}", name, counter));
                  whereArgs.Add(args[i]);
                  counter++;
                  break;
          }
        }
      }

      //Build the WHERE bits
      if (constraints.Count > 0) {
        where = " WHERE " + string.Join(" AND ", constraints.ToArray());
      }
      //probably a bit much here but... yeah this whole thing needs to be refactored...
      if (op.ToLower() == "count") {
        result = Scalar("SELECT COUNT(*) FROM " + TableName + where, whereArgs.ToArray());
      }
      else if (op.ToLower() == "sum") {
        result = Scalar("SELECT SUM(" + columns + ") FROM " + TableName + where, whereArgs.ToArray());
      }
      else if (op.ToLower() == "max") {
        result = Scalar("SELECT MAX(" + columns + ") FROM " + TableName + where, whereArgs.ToArray());
      }
      else if (op.ToLower() == "min") {
        result = Scalar("SELECT MIN(" + columns + ") FROM " + TableName + where, whereArgs.ToArray());
      }
      else if (op.ToLower() == "avg") {
        result = Scalar("SELECT AVG(" + columns + ") FROM " + TableName + where, whereArgs.ToArray());
      } else {

        //build the SQL
        sql = "SELECT TOP 1 " + columns + " FROM " + TableName + where;
        var justOne = op.StartsWith("First") || op.StartsWith("Last") || op.StartsWith("Get") || op.StartsWith("Single");

        //Be sure to sort by DESC on the PK (PK Sort is the default)
        if (op.StartsWith("Last")) {
            orderBy = orderBy + " DESC ";
        } else {
          //default to multiple
          sql = "SELECT " + columns + " FROM " + TableName + where;
        }

        if (justOne) {
          //return a single record
          result = Query(sql + orderBy, whereArgs.ToArray()).FirstOrDefault();
        } else {
          //return lots
          result = Query(sql + orderBy, whereArgs.ToArray());
        }
      }
      return true;
    }
  }
}