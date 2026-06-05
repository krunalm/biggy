# DB-path findings F4 / F5 / F7 — held proposals (not executed)

These three findings cannot be **verified** in the current environment and/or
cross the safety lines in the workflow, so they are written up here as
review-ready proposals instead of being auto-applied. Each section states the
exact change, the expected gain, the **blocker**, and what would unblock it.

Approval note: "all approved" was given in chat. For F4/F5/F7 the blocker is not
"missing approval" — it is "no way to prove behavior is preserved here" (no SQL
Server; F5 also changes executed SQL). Per the safety mandate ("Safety and
behavior preservation override every other goal" + "<90% confident → don't make
it"), I'm holding the commits and surfacing the diffs for your call.

---

## F4 — `BiggyList<T>` upsert is O(n) per `Add` → O(n²) bulk load  ✅ DONE
**File:** `Biggy/BiggyList.cs`
**Status:** ✅ **Implemented and verified** (commit `b361fd7`) after the
`GetHashCode` contract was approved. A `HashSet<T>` membership index makes
`Add`/`Contains` amortized O(1); the five in-repo offender types got a
`GetHashCode` consistent with their `Equals`. DB-free characterization 10/10;
benchmark **98.7 ms → ~1.7 ms** for 5,000 bulk adds (~57×). The text below is the
original analysis, retained for context.

---


### Why the obvious fix is unsafe
The O(n²) comes from `_items.Contains(item)` (O(n)) on every `Add`, plus
`IndexOf`+`RemoveAt`+`Insert` on the update branch. Removing the O(n) membership
scan requires a hash index (`HashSet<T>` / `Dictionary<T,int>`), which uses
`GetHashCode`. **Consumer types override `Equals` without `GetHashCode`** — e.g.
`Tests/Helpers.cs:18` (`Product`) and `Biggy.Tasks/Program.cs`. With a hash index,
two `Equals`-equal items get different hash codes and land in different buckets,
so the upsert/dedup contract silently breaks (duplicates creep in). Confidence in
behavior preservation is well below the 90% bar → **not applied.**

### What does NOT help
Collapsing the double scan (`Contains` then `IndexOf`) into a single `IndexOf`
plus an in-place replace is behavior-preserving but **still O(n) per add** (the
membership scan remains), so it does not address the finding for the common
bulk-load-of-new-items case. Shipping it would be a drive-by tidy (rule 3/4).

### Recommended path (requires an owner decision — changes a contract)
1. Establish the contract: **every type stored in a `BiggyList`/`MassiveList`
   must implement `GetHashCode` consistently with its `Equals`.** Fix the
   in-repo offenders (`Product`, `NWProduct`) and document the requirement.
2. Then introduce a `Dictionary<T,int>` (item → index) maintained alongside
   `_items`, making `Add`/`Update`/`Contains` amortized O(1) and bulk load O(n).
3. Land behind characterization tests for the upsert/dedup semantics (these are
   DB-free and *can* run here once `BiggyList` is linked into the harness).

**Blocker:** equality/hashing **contract change** affecting all consumer types.
**Unblock:** your approval of the `GetHashCode` contract (it is effectively a
breaking expectation for consumers that today rely on reference hashing).

---

## F5 — `DBTable.Insert` uses two server round trips per insert
**File:** `Biggy/Massive.cs:418-451` (SQL Server path); mirror in
`Biggy/MassivePG.cs:39-69` (PG `RETURNING`).

### Current (two round trips)
```csharp
cmd.ExecuteNonQuery();
if (PkIsIdentityColumn) {
  cmd.CommandText = "SELECT SCOPE_IDENTITY() as newID";
  d[PrimaryKeyField] = (int)cmd.ExecuteScalar();
  ...
}
```

### Proposed (one round trip) — DO NOT APPLY UNVERIFIED
```csharp
if (PkIsIdentityColumn) {
  // Append the identity fetch to the INSERT so it executes in a single batch.
  cmd.CommandText += "; SELECT SCOPE_IDENTITY() as newID";
  d[PrimaryKeyField] = Convert.ToInt32(cmd.ExecuteScalar()); // SCOPE_IDENTITY() is numeric/decimal
  ...
} else {
  cmd.ExecuteNonQuery();
}
```

### Why it is held
- **Wire-format change (rule 1):** the executed SQL text changes (batched
  `INSERT … ; SELECT …`). That is exactly the category the workflow forbids
  altering without explicit "approved: breaking".
- **Latent cast:** the existing `(int)cmd.ExecuteScalar()` unboxes the
  `SCOPE_IDENTITY()` result, which SQL Server returns as `numeric(38,0)`
  (boxed `decimal`). `(int)(object)decimalValue` throws `InvalidCastException`.
  Either the original never exercised this against real SQL Server, or the
  provider coerced it. The proposed `Convert.ToInt32(...)` is more robust **but
  is itself a behavior change** that must be validated against a live server.
- **No test net:** there is no SQL Server reachable here, and one cannot be
  installed (SDK/Debian/MS download hosts are firewalled except the NuGet and
  `packages.microsoft.com` package feeds).

**Expected gain:** ~halves insert latency (1 round trip vs 2) on per-row inserts.
**Unblock:** a reachable SQL Server (or accept the wire change explicitly) so an
integration test can prove identical inserted rows + returned identity.

---

## F7 — `MassiveList<T>.Add` is O(n) membership + one DB insert per item
**File:** `Biggy/MassiveList.cs:51-119`
**Status:** ⏸️ **Ready — held on DB verification only.** The `GetHashCode`
contract blocker is now **resolved** (F4 fixed the in-repo offender types). The
in-memory half is a mechanical mirror of the verified F4 change, touching only
the membership ops — **no DB call or SQL is changed.** It is *not* applied
because `MassiveList` cannot even be instantiated here (its constructor calls
`Reload()` → `Model.All<T>()`, a live DB query), so there is no way to run it.

### Exact diff (apply once a SQL Server is reachable to verify)
```csharp
// field, next to _items:
HashSet<T> _index = null;
void RebuildIndex() { _index = _items == null ? new HashSet<T>() : new HashSet<T>(_items); }

// ctor + Reload(): after `_items = this.Model.All<T>().ToList();`
RebuildIndex();

// Update(): on the in-place replace branch, after RemoveAt/Insert
_index.Remove(item); _index.Add(item);
//        on the else branch it calls Add(item), which maintains _index.

// Add(): swap the membership test and maintain the index
if (_index.Contains(item)) { this.Update(item); }
else { this.Model.Insert(item); _items.Add(item); _index.Add(item); }

// AddRange(): Reload() already rebuilds _index.
// Clear():  _items.Clear(); _index.Clear(); this.Model.DeleteWhere("");
// Contains(): return _index.Contains(item);
// Remove():  _index.Remove(item); ... return _items.Remove(item);
```
The per-item `Model.Insert` round trip is API-inherent (use `AddRange` →
`BulkInsert` for bulk loads — already the fast path the `MassiveList` tests use).

### Why it is still held
- **No SQL Server here** to instantiate `MassiveList` or run its integration
  tests; per the safety rules I do not ship unrunnable data-layer changes.

**Unblock:** a reachable SQL Server so the existing `MassiveList` tests
(`Tests/MassiveList.cs`) can confirm identical behavior; then apply the diff.

---

## Summary
| Finding | Localized fix | Status |
|---|---|---|
| F4 | `HashSet<T>` index + `GetHashCode` contract | ✅ **Done & verified** (`b361fd7`), ~57× on bulk add |
| F5 | batch the identity fetch into one round trip | ⏸️ **Held** — wire-format change + `(int)SCOPE_IDENTITY()` cast; **no SQL Server to verify** |
| F7 | mirror F4's index on `MassiveList` | ⏸️ **Ready** — contract resolved; diff above; **held only because `MassiveList` needs a live DB to run** |
