# PERF_REFACTOR: Compiled property accessors

**Class:** LARGE / refactor (approved in chat 2026-06-05).
**Branch:** `claude/perf-audit-rgk0j`
**Scope file:** `Biggy/Extensions/ObjectExtensions.cs` (mapping hot paths only).

## Current state & pain point (evidence)
F1 and F2 cached the reflected `PropertyInfo[]`, which removed per-call array
allocations but left the **dominant cost untouched**: reflective member access.

- `ToSingle<T>` (`ObjectExtensions.cs:91`) calls `PropertyInfo.SetValue` per matched
  column per row — on the read path (`Massive.Query<T>` → `MassiveList.Reload`).
- `ToExpando` (`ObjectExtensions.cs:108`) calls `PropertyInfo.GetValue` per property
  per object — on the write/bulk-insert path (`CreateInsertBatchCommands:337`, etc.).

Benchmark after F1/F2 (10k items/pass, best of 3): F1 `ToList<T>` ~6.07 ms,
F2 `ToExpando` ~4.58 ms. Profile reasoning: with `GetProperties()` now cached,
the remaining per-element work is the reflection `Set/GetValue` call (argument
marshaling + access-check + invoke), which compiled delegates remove.

## Target state
Cache, per `PropertyInfo`, a compiled `Func<object,object>` getter and
`Action<object,object>` setter built with `System.Linq.Expressions` (available on
net45 and net8). Replace the reflective `GetValue`/`SetValue` calls on the two
hot paths with the cached delegates.

```
getter: (object o) => (object)((TDeclaring)o).Prop
setter: (object o, object v) => ((TDeclaring)o).Prop = (TProp)v
```

## Behavior-preservation analysis (the risk surface)
- **Type coercion:** `PropertyInfo.SetValue(o, v)` requires `v` to be assignable/
  unboxable to the property type; it does **not** do `Convert.ChangeType`. The
  compiled `(TProp)v` unbox/cast has the same acceptance set, so the same inputs
  succeed and the same mismatched inputs fail. ✔
- **Failure mode differs only in exception _type_:** feeding `DBNull`/wrong-typed
  value to a value-type property throws `ArgumentException`/`InvalidCastException`
  under reflection vs `InvalidCastException`/`NullReferenceException` under the
  cast. Both still **throw** (no silent data difference). Callers here treat a
  failed map as a hard error, so this is acceptable. Noted as the one observable
  edge.
- **Read-only / indexer / write-only props:** guarded. We only compile a setter
  when `CanWrite` and no index parameters; a getter when `CanRead` and no index
  parameters. Otherwise we **fall back to the original reflective call**, so the
  legacy behavior (including its throw on an indexer) is preserved. This doubles
  as the "keep the old path working" requirement for a LARGE change.

## Migration steps (this doc = the plan)
- [x] Add compiled getter/setter caches + builders (with reflection fallback).
- [x] Route `ToExpando` getter through the cache.
- [x] Route `ToSingle<T>` setter through the cache.
- [x] Characterization suite stays green (10/10) — same behavior.
- [x] Benchmark before/after; keep only if ≥10% on the target metric, else revert.

## Feature-flag / strangler plan
No runtime flag (overkill for a 2-method library hot path). The **reflection
fallback path is retained** for every property that can't be compiled, so the old
mechanism still executes for edge cases — a built-in parallel implementation.
If a regression were found, the single commit reverts cleanly.

## Rollback
Single revertable commit; `git revert <sha>` restores the F1/F2 state.

## Test strategy
- Existing 10 characterization tests (case-insensitive mapping, type round-trip,
  unmatched col/prop, DBNull→null, Expando passthrough, NVC flatten, ToDictionary).
- Add 2 tests: (a) read-only property is not written (fallback path), (b) getter
  round-trips a value-type and reference-type property via `ToExpando`.

## Risk register
| Risk | Mitigation |
|---|---|
| Cast semantics differ from SetValue on odd types | Same acceptance set; characterization covers int/decimal/string/DateTime |
| Read-only/indexer property | Guarded → reflection fallback |
| Expression.Compile cost on first use | Amortized by per-PropertyInfo cache; warmup in benchmark |
| net45 vs net8 behavior delta | Expressions API identical; logic is pure BCL |
