using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Dynamic;
using System.Linq;
using Biggy.Massive;
using Xunit;

namespace Biggy.MassiveList.Characterization {

  // Verifies the fix for the write-path NRE: CreateCommand used to dereference a
  // null connection (conn.CreateCommand()), so CreateInsertCommand/UpdateCommand/
  // DeleteCommand all threw NullReferenceException before any DB work. They now
  // build a real command from an unopened provider connection (no database), with
  // Connection left null for the caller to assign. No SQL Server required.
  public class WritePathCommandBuildingCharacterization {

    private static DBTable NewTable() {
      return new DBTable("__none__", "things", "Id");
    }

    private static IDictionary<string, object> Expando() {
      dynamic ex = new ExpandoObject();
      var d = (IDictionary<string, object>)ex;
      d["Id"] = 0;
      d["Name"] = "x";
      return d;
    }

    [Fact]
    public void CreateInsertCommand_builds_a_command_without_a_connection() {
      var t = NewTable();

      DbCommand cmd = t.CreateInsertCommand(Expando());

      Assert.NotNull(cmd);
      Assert.Null(cmd.Connection);                                   // caller assigns it later
      Assert.Contains("INSERT INTO things", cmd.CommandText);
      // Pk (Id) is an identity column and is dropped; only Name is parameterized.
      Assert.Equal(1, cmd.Parameters.Count);
    }

    [Fact]
    public void CreateUpdateCommand_builds_a_command_without_a_connection() {
      var t = NewTable();

      DbCommand cmd = t.CreateUpdateCommand(Expando(), 5);

      Assert.NotNull(cmd);
      Assert.Contains("UPDATE things", cmd.CommandText);
      Assert.Equal(2, cmd.Parameters.Count);                        // Name + key
    }

    [Fact]
    public void CreateDeleteCommand_builds_a_command_without_a_connection() {
      var t = NewTable();

      DbCommand cmd = t.CreateDeleteCommand(key: 5);

      Assert.NotNull(cmd);
      Assert.Contains("DELETE FROM things", cmd.CommandText);
    }

    [Fact]
    public void CreateCommand_with_a_supplied_connection_is_unchanged() {
      // The non-null path must behave exactly as before: command bound to conn.
      var t = NewTable();
      using (var conn = new System.Data.SqlClient.SqlConnection()) {
        var cmd = t.CreateCommand("SELECT 1", conn);
        Assert.Same(conn, cmd.Connection);
        Assert.Equal("SELECT 1", cmd.CommandText);
      }
    }
  }
}
