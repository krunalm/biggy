using System.Collections.Generic;
using System.Data.Common;
using System.Dynamic;
using Biggy.Massive;
using Xunit;

namespace Biggy.MassiveList.Characterization {

  // Verifies the same write-path fix on the PostgreSQL subclass: PGTable now
  // overrides CreateConnection (not OpenConnection), so the null-connection
  // command-building path builds Npgsql commands without a database.
  public class PgWritePathCharacterization {

    [Fact]
    public void PGTable_CreateInsertCommand_builds_an_npgsql_command_without_a_connection() {
      var t = new PGTable("__none__", "things", "Id");
      dynamic ex = new ExpandoObject();
      var d = (IDictionary<string, object>)ex;
      d["Id"] = 0;
      d["Name"] = "x";

      DbCommand cmd = t.CreateInsertCommand(ex);

      Assert.NotNull(cmd);
      Assert.IsType<Npgsql.NpgsqlCommand>(cmd);     // provider-correct command
      Assert.Contains("INSERT INTO things", cmd.CommandText);
      Assert.Equal(1, cmd.Parameters.Count);
    }
  }
}
