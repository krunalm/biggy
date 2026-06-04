# Performance Audit — Biggy

**Date:** 2026-06-04
**Branch:** `claude/perf-audit-rgk0j`
**Status:** 🟥 **AUDIT ONLY — auto-execution (Step 7) BLOCKED.** No code changed.
Reason: the safety net required by the workflow cannot be established in this
environment (see "Safety net status"). All findings below are static analysis.

---

## Step 1 — Detected stack

| Signal | Detected | Evidence |
|---|---|---|
| **.NET (Framework)** | ✅ **.NET Framework 4.5** | `Biggy.sln`; every `*.csproj` is legacy non-SDK MSBuild (`ToolsVersion="4.0"`, `<TargetFrameworkVersion>v4.5</TargetFrameworkVersion>`) |
| ASP.NET (MVC 5) | ✅ (sample app) | `Web/Web.csproj` references `System.Web.Mvc 5.0.0`, OWIN, EF6, Identity |
| SQL Server | ✅ (runtime target) | `Massive.cs:11` `using System.Data.SqlClient;`; `Massive.cs:136` `new SqlConnection(...)`; SQL-Server-only `SELECT TOP`, `SCOPE_IDENTITY()` |
| PostgreSQL | ✅ (alternate provider) | `Biggy/MassivePG.cs` (`NpgsqlConnection`, `LIMIT`, `RETURNING`); `Npgsql 2.0.14.3` |
| Go / Rust / Android / Python / Supabase / SQLite / SQL Server `.sqlproj` | ❌ | no manifests found |

Projects: **Biggy** (class library — the audit target), **Tests** (xUnit 1.9.2),
**Biggy.Tasks** (console playground), **Web** (ASP.NET MVC sample).

### Stack tooling report
- **Test framework:** xUnit **1.9.2** (`Tests/packages.config`). Ancient; needs the
  legacy `xunit.console` runner — no `dotnet test` support.
- **CI:** ❌ none (`.github/workflows` absent).
- **Benchmark infrastructure:** ❌ none (no BenchmarkDotNet, no `*.Benchmarks` project).
- **Coverage tooling:** ❌ none. Estimated **effective coverage of the Biggy library
  hot paths ≈ 0%** in any runnable form here (see below).

---

## Step 2 — Safety net status 🟥 **CANNOT ESTABLISH — STOP**

This is the decisive finding. Per safety rule 5 and Step 2
("If the test suite is failing on a clean checkout, STOP"), I did not proceed to
any code change. Blockers:

1. **No build/test toolchain present.** `dotnet`, `mono`, `msbuild`, `xbuild`,
   `nuget` are all absent from the container. This is a **.NET Framework 4.5**
   codebase, which does not build or run natively on Linux.
2. **No restored packages** (`packages/` absent) and **no CI** to lean on.
3. **The DB-facing tests are integration tests against an external SQL Server that
   does not exist here.** `Tests/App.config` points at
   `Data Source=XIVMAIN;Initial Catalog=NORTHWIND;Integrated Security=True` — a
   specific developer machine with Windows Integrated Auth. `Tests/MassiveSetup.cs`
   issues SQL-Server-only DDL (`int IDENTITY(1,1)`, `Money`/`Text` types,
   `INFORMATION_SCHEMA … TABLE_SCHEMA='dbo'`). These cannot run in this environment.
4. **The library's hot paths have no isolated unit coverage.** The reflection
   mapping core (`ToSingle<T>`, `ToExpando`, `RecordToExpando`) is exercised *only*
   indirectly through the SQL-Server integration tests. The only filesystem-only
   tests are `Reads.cs`, `Writes.cs`, `Dynamics.cs` (cover `BiggyList`/`BiggyDB`
   JSON store), and even those cannot be executed here without a toolchain.

**Commands that *would* run the suite (documented, not runnable here):**
```
# Windows / Visual Studio toolchain assumed:
nuget restore Biggy.sln
msbuild Biggy.sln /p:Configuration=Release
packages\xunit.runners.1.9.2\tools\xunit.console.clr4.exe Tests\bin\Release\Tests.dll
# (DB tests additionally require a reachable SQL Server matching App.config)
```

**Consequence for this audit:** Step 7's loop *requires* (a) the full suite green
on a clean checkout, and (b) before/after benchmarks for each fix. Neither is
possible here, so **every finding — even SAFE ones — is held at "proposed, not
executed."** Nothing in this repo was modified except the addition of this file.

### What would unblock execution (needs your go-ahead — all touch infra/tests)
- **A**: Provide a runnable toolchain (e.g. retarget the **Biggy** library to
  `netstandard2.0`/SDK-style, or install Mono) — this is itself a build-system
  change, out of scope for a perf pass without approval.
- **B**: Add **DB-free characterization unit tests** for the reflection mapping
  hot paths (`ToSingle<T>`, `ToExpando`, `RecordToExpando`) using an in-memory
  `IDataReader` fake — no SQL Server needed. These are the tests that must exist
  *before* touching F1/F2. See "Characterization tests" below.
- **C**: Stand up a containerized SQL Server (or point the PG provider at a local
  Postgres) and rewrite `App.config` + `MassiveSetup` DDL to match — large, and
  changes test infrastructure.

I recommend **B** first: it is the smallest safe net that covers the two
highest-impact findings without any external dependency.

---

## Critical paths and their test coverage

| Critical path | Entry point | Runnable coverage here | Notes |
|---|---|---|---|
| Row → object materialization (read hot path) | `DBTable.Query<T>` → `ObjectExtensions.ToSingle<T>` (`ObjectExtensions.cs:90`) | ❌ none (DB-only, indirect) | **Dominant read cost.** Tests read 10k rows (`MassiveList.cs:21 _qtyCrapTons = 10000`). |
| Object → params (write hot path) | `DBTable.Insert/Update/BuildCommands` → `ObjectExtensions.ToExpando` (`ObjectExtensions.cs:107`) | ❌ none (DB-only, indirect) | Reflection per object on every write. |
| Row → dynamic | `DBTable.Query` → `RecordToExpando` (`ObjectExtensions.cs:73`) | ❌ none (DB-only, indirect) | Per-row Expando build. |
| In-memory list upsert | `BiggyList<T>.Add` (`BiggyList.cs:106`) | ✅ logic covered by `Writes.cs` (not runnable here) | O(n) per add → O(n²) bulk. |
| JSON persistence | `BiggyList<T>.Save` (`BiggyList.cs:196`) | ✅ covered by `Writes.cs` (not runnable here) | Full-file rewrite each save. |
| DB list load | `MassiveList<T>.Reload` (`MassiveList.cs:50`) | ❌ none (DB-only) | Calls `All<T>()` → hot path F1. |

---

## Findings table

| # | Finding | File:Line | Impact | Effort | Class | Priority | Auto-exec? |
|---|---|---|---|---|---|---|---|
| F1 | `ToSingle<T>` does uncached reflection + O(props×fields) name match **per row** | `Extensions/ObjectExtensions.cs:90-104` | High (read hot path; 10k-row reads) | Low–Med | MODERATE | 1 | **N** (no test net / benchmark) |
| F2 | `ToExpando` does uncached `GetProperties()` **per object** on every write | `Extensions/ObjectExtensions.cs:107-124` | High (write hot path) | Low–Med | MODERATE | 2 | **N** (no test net / benchmark) |
| F3 | `RecordToExpando` rebuilds field metadata per row | `Extensions/ObjectExtensions.cs:73-83` | Med (dynamic read path) | Low | SAFE→MODERATE | 3 | **N** (no test net / benchmark) |
| F4 | `BiggyList.Add` upsert is O(n) (`Contains`+`IndexOf`+`RemoveAt`+`Insert`) → O(n²) bulk | `BiggyList.cs:97-127` | Med (bulk in-memory load) | Med | Needs discussion | 4 | **N** (semantics risk: `Equals` w/o `GetHashCode`) |
| F5 | `Insert` makes **two** DB round trips (`ExecuteNonQuery` + `SELECT SCOPE_IDENTITY()`) | `Massive.cs:427-434` | Med (per-insert latency) | Med | Needs discussion | 5 | **N** (changes SQL/wire behavior) |
| F6 | `BulkInsert` computes `requiredParams`/`batchCounter` then never uses them (dead) | `Massive.cs:460-466` | Negligible | Low | SAFE (micro) | 6 | **N** (de-prioritized micro) |
| F7 | `MassiveList.Add` does a DB insert **and** O(n) `Contains` per single item | `MassiveList.cs:64-86` | Med (per-item loops) | Med | Needs discussion | 7 | **N** (API-inherent; `AddRange` exists) |

See "Out of scope" for correctness issues spotted in passing (F11 truncation bug,
F5/PG `RETURNING` smell, unused `JsonExtensions`) — **not touched** per rule 3.

---

## Per-stack detailed findings (.NET)

### F1 — `ToSingle<T>`: per-row reflection + nested-loop column mapping  ⭐ top priority
**Evidence** (`Extensions/ObjectExtensions.cs:90-104`):
```csharp
public static T ToSingle<T>(this IDataReader rdr) where T : new() {
  var item = new T();
  var props = item.GetType().GetProperties();          // (a) reflection EVERY row
  foreach (var prop in props) {
    for (int i = 0; i < rdr.FieldCount; i++) {          // (b) O(props × fields) EVERY row
      if (rdr.GetName(i).Equals(prop.Name, StringComparison.InvariantCultureIgnoreCase)) {
        prop.SetValue(item, rdr.GetValue(i));           // (c) reflective set EVERY cell
      }
    }
  }
  return item;
}
```
Called once per row from `DBTable.Query<T>` (`Massive.cs:70-85`), which backs
`All<T>()` and therefore `MassiveList.Reload()`. For an N-row result with P
properties and F fields this is **N× `GetProperties()` calls + N·P·F culture-aware
string comparisons + N·(matched) reflective `SetValue`s**. The test set is 10,000
rows (`MassiveList.cs:21`), so the constant factors dominate the materialization.

**Proposed fix (behavior-preserving):**
1. Cache `PropertyInfo[]` per `Type` in a `static ConcurrentDictionary<Type,
   PropertyInfo[]>` (eliminates (a)).
2. Build the **column-ordinal → PropertyInfo** map **once per reader** (before the
   row loop) instead of the nested compare per row (turns (b) from O(N·P·F) into
   O(P·F) once + O(F) per row). Same matching rule (case-insensitive name).
This preserves the exact set of mapped columns and values; it only removes
repeated work. **Classified MODERATE** (it introduces a cache + changes the
internal mapping shape) — requires the F1/F2 characterization tests first and a
BenchmarkDotNet before/after. **Not executed.**

### F2 — `ToExpando`: per-object reflection on every write
**Evidence** (`Extensions/ObjectExtensions.cs:107-124`): `o.GetType().GetProperties()`
on every call. `ToExpando` is invoked by `Insert` (`Massive.cs:419`), `Update`
(`:486,:501`), `BuildCommands` (`:150,:152`), `CreateInsertBatchCommands` (`:307,:337`),
and `GetPrimaryKey`/`HasPrimaryKey` via `ToDictionary` (`:46,:56`). On a bulk insert
every row pays a fresh `GetProperties()`.
**Proposed fix:** reuse the same per-`Type` `PropertyInfo[]` cache as F1. Identical
output Expando. **MODERATE, not executed** (same gating as F1).

### F3 — `RecordToExpando`: per-row metadata
**Evidence** (`Extensions/ObjectExtensions.cs:73-83`): builds the dictionary by
calling `rdr.GetName(i)` per column per row. Lower impact than F1 (no type
reflection), but the column-name array can be hoisted once per reader.
**SAFE→MODERATE, not executed.**

### F4 — `BiggyList` upsert is O(n) per add
**Evidence** (`BiggyList.cs:106-127` + `:97-105`): `Add` calls `_items.Contains(item)`
(O(n)); on a hit `Update` does `IndexOf` (O(n)) + `RemoveAt` (O(n)) + `Insert` (O(n)).
Loading n items via repeated `Add` is **O(n²)**.
**Why not auto-executed:** the natural fix (a hash-based index) depends on
`GetHashCode`, but user/test types override `Equals` **without** `GetHashCode`
(`Tests/Helpers.cs:18`, `Biggy.Tasks/Program.cs`), so a `HashSet`/`Dictionary`
keyed on the item would **break upsert semantics**. Confidence in behavior
preservation < 90%. → **Needs discussion.**

### F5 — `Insert` uses two round trips for identity retrieval
**Evidence** (`Massive.cs:424-446`): `cmd.ExecuteNonQuery()` then a second
`cmd.ExecuteScalar()` of `SELECT SCOPE_IDENTITY()` on the same connection — two
server round trips per insert. Combining into a single batched
`INSERT …; SELECT SCOPE_IDENTITY();` would halve round trips but **changes the SQL
text/wire behavior** and interacts with the PG override (`MassivePG.cs:55-66`, which
re-assigns `cmd.CommandText = " RETURNING …"` — see Out of scope). → **Needs
discussion** (potential BREAKING on wire format).

### F6 — Dead computation in `BulkInsert`
**Evidence** (`Massive.cs:460-466`): `itemParameterCount`, `requiredParams`,
`batchCounter` are computed and never used; real batching lives in
`CreateInsertBatchCommands`. Removing them is a **micro** cleanup with no runtime
benefit — **de-prioritized**, and removing code requires approval (rule 2) anyway.

### F7 — `MassiveList.Add` single-item cost
**Evidence** (`MassiveList.cs:64-86`): each `Add` performs `_items.Contains` (O(n))
**and** a DB `Insert` (one round trip). For loading many rows one-by-one this is
N round trips + O(n²) membership checks. `AddRange` → `BulkInsert` (`MassiveList.cs:88`,
`Massive.cs:460`) already exists for the bulk case. This is API-inherent; not a
localized fix. → **Needs discussion.**

---

## Plan (checklist, grouped by class, ordered by impact-to-effort)

> ⚠️ **All boxes remain unchecked: execution is blocked until a safety net exists.**

### Pre-req (must land first)
- [ ] Add DB-free **characterization tests** for `ToSingle<T>`, `ToExpando`,
      `RecordToExpando` (in-memory `IDataReader` fake). Must pass on **unmodified**
      code. *(Needs your approval — adds to test project.)*
- [ ] Add a **BenchmarkDotNet** project (or a `Stopwatch` micro-harness) for the
      read/write mapping paths to capture baselines. *(Needs your approval — new project.)*

### MODERATE (auto-eligible *only after* the pre-reqs + green suite)
- [ ] **F1** — cache `PropertyInfo[]` per type + per-reader ordinal map in `ToSingle<T>`.
- [ ] **F2** — reuse the per-type `PropertyInfo[]` cache in `ToExpando`.
- [ ] **F3** — hoist column-name lookups in `RecordToExpando`.

### Needs discussion (do not execute)
- [ ] **F4** — `BiggyList` O(n²) upsert (blocked by `Equals`/`GetHashCode` mismatch).
- [ ] **F5** — collapse `Insert` two round trips into one (wire-format risk).
- [ ] **F7** — per-item `MassiveList.Add` cost (API-inherent).

### Micro / declined
- [ ] **F6** — remove dead vars in `BulkInsert` (negligible; deletion needs approval).

---

## Characterization tests to add BEFORE any fix (proposed, not written)

Targeting the weakly-covered hot paths so F1–F3 become safe to touch. All are
**DB-free** (use a hand-rolled `IDataReader` stub), so they run anywhere a
toolchain exists:

1. `ToSingle<T>` maps columns to properties **case-insensitively** (e.g. reader
   column `transactionid` → property `TransactionId`).
2. `ToSingle<T>` leaves unmatched properties at their default and ignores
   unmatched columns.
3. `ToSingle<T>` round-trips representative types (`int`, `decimal`, `string`,
   `DateTime`) incl. `DBNull` → null behavior **as currently coded**
   (note: `ToSingle` currently passes `DBNull` straight to `SetValue`; the
   characterization test must lock in *today's* behavior, including any throw).
4. `ToExpando(poco)` produces a key per public property with identical values;
   `ToExpando(expando)` returns the same instance (`ObjectExtensions.cs:110`);
   `ToExpando(NameValueCollection)` flattens keys (`:111-114`).
5. `RecordToExpando` produces one key per field with `DBNull` → null
   (`ObjectExtensions.cs:79`).

These must be **green on the unmodified code** and committed as their own commit
before F1/F2/F3 are attempted.

---

## Measurement plan (per fix)

| Fix | Baseline metric | Tool | Pass bar |
|---|---|---|---|
| F1 | ns/row + allocations materializing 10k rows into `List<T>` | BenchmarkDotNet (`[MemoryDiagnoser]`) over an in-memory `IDataReader` | ≥10% time **and**/or allocation reduction, suite still green |
| F2 | ns/object + allocations for `ToExpando` over 10k objects | BenchmarkDotNet | ≥10% reduction, suite green |
| F3 | ns/row for `RecordToExpando` over 10k rows | BenchmarkDotNet | ≥10% reduction, suite green |

Anything < 10% or within noise → revert and mark "Not worth it" (Step 7.7).

---

## Risks register & rollback notes

| Risk | Affected | Mitigation | Rollback |
|---|---|---|---|
| Property cache changes mapping order/visibility (e.g. inherited/indexer props) | F1, F2, F3 | Cache exactly `GetProperties()` default set; characterization tests assert parity | Single-commit revert |
| `DBNull`/null edge behavior changes during F1 refactor | F1 | Characterization test #3 pins current behavior first | Single-commit revert |
| Hash-based upsert breaks `Equals`-without-`GetHashCode` types | F4 | **Not executed** | n/a |
| Wire-format change from batching identity SELECT | F5 | **Not executed** | n/a |
| Static cache + concurrency | F1, F2, F3 | Use `ConcurrentDictionary`; cached `PropertyInfo[]` is read-only | Single-commit revert |

Every proposed change is scoped to a single file (`ObjectExtensions.cs`) and a
single commit, satisfying the "revertable as one commit" rule.

---

## Out of scope (drive-by observations — NOT touched, per rule 3)

- **CORRECTNESS — `SaveAsync` can corrupt the JSON file** (`BiggyList.cs:184-189`):
  `File.OpenWrite` opens **without truncating**, and writes via
  `Encoding.Default.GetBytes`. If the new JSON is shorter than the existing file,
  stale trailing bytes remain → invalid JSON. (`Save` at `:196` uses
  `File.CreateText`, which truncates, and is fine.) This is a behavior bug, not a
  perf issue — flagging only.
- **CORRECTNESS — PG `Insert` identity retrieval looks broken** (`MassivePG.cs:55-66`):
  after `ExecuteNonQuery`, it sets `cmd.CommandText = " RETURNING <pk> as newId"`
  and `ExecuteScalar()`s a statement that is only a `RETURNING` fragment. Worth a
  closer look, but it is a correctness/provider concern, not perf.
- **`Query<T>(sql, args)` overload doesn't dispose the reader/command**
  (`Massive.cs:70-77`): only the connection is in a `using`; the DB-connection
  overload at `:79-85` correctly wraps the reader. Resource-handle smell.
- **`Tests/Helpers.cs:18` `Product.Equals` overrides without `GetHashCode`** (and
  `Equals` casts unconditionally → throws on cross-type compare). Test/user code.
- **`JsonExtensions` (`Extensions/JsonExtensions.cs`) uses `JavaScriptSerializer`**
  (slower than Newtonsoft, which the core already uses) and appears **unused** by
  the library. Candidate for removal — but deletion needs approval (rule 2).
- **`Encoding.Default`** usage (`BiggyList.cs:186`) is culture/OS dependent; prefer
  explicit UTF-8 — correctness/portability, not perf.

## Needs discussion (low-confidence / behavior-sensitive)
- **F4** (BiggyList O(n²) upsert) — needs a `GetHashCode` contract decision.
- **F5** (collapse Insert round trips) — touches SQL text / could be BREAKING.
- **F7** (per-item `MassiveList.Add`) — API-shaped cost.
- **Toolchain/test-net strategy (A/B/C above)** — needed before *anything* executes.

## Awaiting approval (LARGE refactors)
- None proposed. No LARGE/architectural item is recommended at this stage; the
  meaningful wins (F1–F3) are MODERATE and localized once a test net exists.

---

## Bottom line
The two highest-value, lowest-risk wins are **F1** and **F2** (kill per-row/per-object
reflection on the read/write hot paths). They are MODERATE and localized to
`ObjectExtensions.cs`, but the workflow forbids executing them until (1) a runnable
toolchain exists and (2) DB-free characterization tests + a benchmark establish a
verifiable baseline. **I have stopped at Step 7 and changed no code.** Tell me how
you'd like to proceed on the toolchain/test-net (options A/B/C) and I'll continue.
