using System;
using System.Collections.Generic;
using System.Dynamic;
using Biggy.Massive;
using Xunit;

namespace Biggy.MassiveList.Characterization {

  // Documents a PRE-EXISTING defect discovered while investigating F5 (collapse
  // DBTable.Insert's two round trips). The Massive write path cannot build a
  // command at all: CreateInsertCommand -> CreateCommand(stub, null) ->
  // conn.CreateCommand() dereferences a NULL connection and throws.
  //
  // Consequence: Insert / Save(Execute) / BulkInsert all throw NullReferenceException
  // before any database round trip occurs, so F5's round-trip optimization is moot.
  // Fixing this is a behavior change (NRE -> working insert) and needs a real
  // SQL Server to validate the resulting SQL/identity behavior, so it is NOT
  // done here. This test pins the CURRENT behavior so the regression is visible
  // if/when the write path is repaired.
  public class WritePathDefectCharacterization {

    [Fact]
    public void CreateInsertCommand_currently_throws_NRE_due_to_null_connection() {
      var t = new DBTable("__none__", "things", "Id");
      dynamic ex = new ExpandoObject();
      var d = (IDictionary<string, object>)ex;
      d["Id"] = 0;
      d["Name"] = "x";

      // CreateCommand(sql, null) -> null.CreateCommand(). Pinning the defect:
      Assert.Throws<NullReferenceException>(() => t.CreateInsertCommand(ex));
    }
  }
}
