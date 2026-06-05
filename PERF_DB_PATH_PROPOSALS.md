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

## F4 — `BiggyList<T>` upsert is O(n) per `Add` → O(n²) bulk load
**File:** `Biggy/BiggyList.cs:97-127`

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
**File:** `Biggy/MassiveList.cs:64-86`

### Observation
`Add` does `_items.Contains(item)` (O(n)) and a `Model.Insert(item)` (one DB
round trip) per call; loading many rows one-by-one is N round trips + O(n²)
membership checks. A bulk path already exists (`AddRange` → `BulkInsert`,
`MassiveList.cs:88`, `Massive.cs:460`).

### Why it is held
- The O(n) membership half has the **same `GetHashCode` blocker as F4**.
- The per-item DB insert is API-inherent and only avoidable by steering callers
  to `AddRange`/`BulkInsert` — a usage change, not a localized fix.
- **No SQL Server here** to verify any change to this path.

### Recommended path
- Documentation/API guidance: prefer `AddRange` for bulk loads (already the
  faster path; the `MassiveList` tests use it for the 10k-row case).
- If `Add`-loops must be fast, that depends on F4's contract decision (hash
  index) plus a batched-insert buffer — a larger design, owner decision needed.

**Unblock:** F4's `GetHashCode` contract decision + a reachable SQL Server.

---

## Summary
| Finding | Localized fix exists? | Behavior-preserving here? | Verifiable here? | Status |
|---|---|---|---|---|
| F4 | only an O(1)-index fix, needs `GetHashCode` contract | ❌ (breaks dedup for current consumers) | ✅ (DB-free) but fix itself unsafe | **Held — contract decision** |
| F5 | yes (batch the identity fetch) | ❓ (wire change + cast) | ❌ (no SQL Server) | **Held — needs DB + breaking approval** |
| F7 | no (API-inherent + F4 blocker) | ❌ | ❌ (no SQL Server) | **Held — depends on F4 + DB** |
