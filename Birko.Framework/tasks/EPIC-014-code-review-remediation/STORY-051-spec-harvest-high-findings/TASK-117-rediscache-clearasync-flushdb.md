---
id: TASK-117
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
pr: 333ba76   # + Birko.Caching@26e6a3f (ICache doc), Birko.Caching.Redis.Tests@1ae2c21 (suite)
github-issue: null
jira-key: null
findings: [SH-H006]
---

# `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set

## Context

`../Birko.Caching.Redis/RedisCache.cs:187` — **CONFIRMED**, read verbatim.

With no `KeyPrefix`, `ClearAsync` takes the else branch to
`server.FlushDatabaseAsync(_settings.Database)` — which destroys **every key in that logical database**, not
just this cache's entries.

`RedisSettings.KeyPrefix` **defaults to null**, so this is the default path, not an edge case. And the keys
it destroys belong to siblings that share the connection by design: `Birko.MessageQueue.Redis`,
`Birko.BackgroundJobs.Redis`, and Redis sync stores. A cache clear silently drops queued messages and
pending background jobs.

`ICache.ClearAsync` is documented as *"Removes all entries from the cache"* — the cache, not the database.
The implementation is wider than its own contract.

### Re-verified 2026-08-12 (step 3) — holds verbatim, plus one sibling and one rescope

**Holds as code, but its stated mechanism was wrong — corrected at the close gate.** `RedisCache.cs:177-189`
is exactly as filed and `RedisSettings.KeyPrefix` (`../Birko.Redis/RedisSettings.cs:25`) is an unassigned
`string?`, so the branch is the default. But `FLUSHDB` is **admin-gated** by StackExchange.Redis and
`GetConnectionString()` never emits `allowAdmin=true` — measured by reflecting the shipped 2.8.41:
`Message.IsAdmin` is `true` for `FLUSHDB` and `KEYS`, `false` for `SCAN`/`DEL`/`UNLINK`, and
`ConfigurationOptions.Parse(new RedisSettings("localhost").GetConnectionString()).AllowAdmin` is `False`. A
grep for `allowAdmin` across `Framework`, `Framework.Tests` and `Consumers` matches only the
StackExchange.Redis binary. So:

- on a **settings-built** cache, `ClearAsync` with no prefix **threw** `RedisCommandException` — self-reporting,
  not silent destruction;
- it destroyed the database only for a consumer whose `RedisSettings.RawConnectionString` carries
  `allowAdmin=true` (a supported path — `GetConnectionString()` returns it verbatim);
- **the door that destroyed data silently on every configuration is `RemoveByPrefixAsync("")`** — `SCAN "*"` +
  `DEL`, neither admin-gated — which this section originally filed as *secondary*. It was primary.

The defect and its severity survive the correction; only the command changes. **The step-2 ranking rationale
does not fully survive**: "the default path destroys data and reports success" was true of the prefix door and
false of the `FLUSHDB` door it was written about. The pick was still correct — a silent whole-database delete
reachable from a documented cache API outranked the runner-up on every key — but the reason has been restated
rather than left flattering.

**Sibling pulled into scope (same root cause, same function family) — and it turned out to be the live one.**
`RemoveByPrefixAsync("")` on an unprefixed cache computes `GetFullKey("") == ""` and therefore scans
`pattern: "*"` — deleting every key in the database through a second door, by `SCAN`+`DEL` instead of
`FLUSHDB`. `ClearAsync` is only the *documented* way to reach it. The single root cause is that **an
unprefixed `RedisCache` has no key space of its own**: its keys are written bare, so they are byte-for-byte
indistinguishable from `Birko.MessageQueue.Redis`'s and `Birko.BackgroundJobs.Redis`'s. Both doors are fixed
together.

**Acceptance criterion 1 was unsatisfiable and has been corrected** (before writing code). It asked that an
unprefixed `ClearAsync` *"removes this cache's entries and leaves other keys intact"*. There is no such set
to remove — "this cache's entries" is undefined without a prefix, and two unprefixed `RedisCache` instances
on one database are literally the same key space. The three ways to invent one were each rejected:

- **An owned-key index** (a Redis SET appended to on every `SetAsync`) — the index needs a key name, which
  is the key-layout change option 2 was rejected for; it costs a round-trip per write; and Redis expiry does
  not remove members, so the index grows without bound and `ClearAsync` degrades to deleting mostly-dead ids.
- **Scan a default prefix anyway** — finds nothing, because keys were written bare. `ClearAsync` becomes a
  **silent no-op that reports success**, which is the wrong answer dressed as the right one.
- **Default `KeyPrefix` non-null** (option 2) — silent cache miss for every existing consumer on deploy.

So the operation **refuses**, per § Conventions: *"A mapper that cannot express something refuses; it never
drops it quietly"*, and fail-fast is legitimate here because two opt-outs exist and are checked first —
configure a `KeyPrefix`, or call the explicitly named flush door. This is the same shape as TASK-109's
`WholeTableWriteException`: a write whose scope reduces to "everything" is refused at the boundary, and the
deliberate caller says so out loud.

**Sweep result (criterion 6): negative.** `grep -rn "FlushDatabase\|FlushAll" --include=*.cs` across all 100+
sibling projects returns **exactly this one line**. The pattern was not copied to
`Birko.BackgroundJobs.Redis`, `Birko.MessageQueue.Redis` or the Redis sync stores. Nothing to file.

**One caller is affected:** `HybridCache.ClearAsync` (`../Birko.Caching.Hybrid/HybridCache.cs:234`) forwards
to its L2. With `FallbackToL1OnL2Failure` set it will *swallow* the refusal and clear L1 only — that is the
documented Hybrid contract for any L2 failure, not a new hole, but it means an unprefixed Redis L2 goes from
"flushed the whole database" to "silently not cleared". Recorded, not changed: changing it would alter the
Hybrid fallback contract for every failure mode, which is a separate decision.

## Approach

`ClearAsync` must only ever remove keys this cache owns. The options, in preference order:

1. **Require a prefix for `ClearAsync` to be meaningful** — track an owned-key namespace and delete by it
   (`SCAN` + `UNLINK` in batches, not `KEYS`, which blocks the server). Works with no configuration change
   and stops being destructive by default.
2. **Default `KeyPrefix` to something non-null**, so the prefixed branch is always taken. Simpler, but it
   changes key layout for every existing consumer — a silent cache miss on deploy, and worse if a consumer
   reads those keys from elsewhere.
3. Throw when `ClearAsync` is called with no prefix. Honest, but breaks a working (if dangerous) call.

Prefer (1). Whatever is chosen, **`FLUSHDB` should not be reachable from a cache API**; if a caller genuinely
wants to flush a database, that deserves an explicitly named method that says so.

Check the other Redis-backed projects for the same pattern before fixing — a `FLUSHDB` behind a
component-scoped "clear" is the kind of thing that gets copied, and this is a family with four Redis
backends.

## Acceptance criteria

> Criterion 1 was **corrected in step 3** — see § *Re-verified* above. The original asked for an
> unprefixed clear to delete "this cache's entries", a set that does not exist. It is replaced by a refusal,
> which satisfies the criterion's actual intent (other keys survive) by a stronger route: nothing is sent.

- [x] `ClearAsync` with no `KeyPrefix` **refuses**, throwing `WholeDatabaseDeleteException` **before opening a
      connection**, so no key in the database is touched — asserted offline, no live server needed
- [x] The exception names both opt-outs: configure a `KeyPrefix`, or call the explicit flush door
- [x] `RemoveByPrefixAsync("")` on an unprefixed cache — the same whole-database delete through a second door
      — refuses identically
- [x] `ClearAsync` with a `KeyPrefix` is unchanged, and is **not** caught by the refusal
- [x] `RemoveByPrefixAsync` with a non-empty prefix is unchanged even with no `KeyPrefix` (the pattern is
      bounded, so ownership is not in question)
- [x] **Added at the close gate:** the literal prefix is glob-escaped, so a metacharacter cannot turn a
      bounded prefix back into a whole-database pattern and slip past the emptiness guard
- [x] **Added at the close gate:** the opt-out the refusal names actually opens — `KeyPrefix` is named first
      because it works on every configuration, and `FlushDatabaseAsync`'s `allowAdmin=true` precondition is
      stated in the message, the XML doc and a test, so the guard is not a wall wearing a door's label
- [x] **Added at the close gate:** the regression suite touches no live Redis — it targets TEST-NET-1, so the
      "not refused" tests cannot delete a developer's or a fixture's keys while running past the guard
- [x] Keys belonging to `Birko.MessageQueue.Redis` / `Birko.BackgroundJobs.Redis` survive a cache clear
- [x] `FLUSHDB` is not reachable from `ICache`; the flush door is `RedisCache.FlushDatabaseAsync`, declared
      on the concrete class only, and documents what it destroys — **asserted by reflection**
      (`FlushDatabaseAsync_IsNotReachableThroughICache`), not merely by construction, so widening `ICache`
      later fails loudly
- [x] Key enumeration uses `SCAN`, not `KEYS`, and deletes in batches — **already true** via
      `server.KeysAsync` (CR-L040); now stated in the doc comment so it is not lost to a refactor
- [x] The other Redis-backed projects are checked for the same pattern, and any hit is filed or fixed —
      **negative sweep, nothing to file**
- [x] Regression tests in `Birko.Caching.Redis.Tests`, offline (the guard precedes every Redis call, the same
      property the CR-M034 cancellation suite relies on), so the STORY-042 Docker gate is not needed
- [x] `/specs regen` for `caching`, spec diff reviewed

## Out of scope

- `SH-H004` / `SH-H005` (cache keys derived from `Expression.ToString()` colliding across tenants; SQL cache
  keys carrying no database/connection/tenant identity) — same area, unverified, and **arguably higher
  impact than this one**. Verify them next.
- `SH-H007` (`UpdateAsync(filter, Action<T>)` writing back a stale cached snapshot).

## Human test plan

N/A — fully covered by automated tests, and the **reason is the shape of the fix, not a concession**.

The plan as filed required a live or faked Redis, because it assumed a fix that *deletes selectively* and
therefore needs a foreign key written beforehand to prove the deletion was scoped. The delivered fix
**refuses before opening the connection**, so there is no command to observe and nothing for a human to
inspect on a server. `ClearAsync_WithNoKeyPrefix_RefusesInsteadOfFlushingTheDatabase` runs against an
unreachable `localhost:6379` and gets `WholeDatabaseDeleteException`, **not** `RedisConnectionException` —
which is simultaneously the ordering proof (the guard precedes `GetServer()`) and *stronger* evidence that
other components' keys survive than any post-hoc key check could be: the keys survive because no command is
sent. The warning the plan
attached ("do not downgrade to a mock that cannot observe `FLUSHDB`") is honoured: nothing here is mocked.

The prefixed path is unchanged code, and its end-to-end behaviour against a live server is pre-existing —
untested before this task and still untested, gated on STORY-042 (no Docker in this environment). That is not
a manual step this task created or can discharge.

## Outcome

**What was broken.** A Redis cache with no `KeyPrefix` — the default, because the setting has no default value
— could delete every key in the Redis database, including the queued messages and pending background jobs of
other Birko components sharing it. Two doors did this: `RemoveByPrefixAsync("")` silently, on every
configuration; and `ClearAsync` via `FLUSHDB`, for any consumer whose connection string enables admin mode
(elsewhere that branch threw, which is how it survived unnoticed).

**What the fix is.** An unprefixed `RedisCache` writes bare keys, so it has no key space of its own and
cannot be asked to clear "just its own entries" — that set does not exist. So the operation now **refuses**,
throwing `WholeDatabaseDeleteException` before it opens a connection, and the message names the two ways to
proceed deliberately: configure a `KeyPrefix`, or call the new `RedisCache.FlushDatabaseAsync()`. `FLUSHDB` is
no longer reachable through `ICache` at all — a cache-shaped contract should not be able to empty a database.
The same refusal covers `RemoveByPrefixAsync("")`, which reached the identical whole-database delete through a
second door by scanning `"*"`. Prefixed caches are unaffected, and so is `RemoveByPrefixAsync("user:")` on an
unprefixed cache, because a caller-supplied prefix bounds the pattern on its own.

**The close gate found a bypass of the guard itself, and it is now part of the fix.** The inline security pass
asked the one question that matters about a scope guard — *can it be walked past?* — and it could, by one
character. `ResolveOwnedKeyPattern` interpolated the literal prefix into a Redis `MATCH` pattern **unescaped**,
so `RemoveByPrefixAsync("*")` on an unprefixed cache resolved to `"**"`: non-null, therefore past the emptiness
guard, and `"**"` matches **every key in the database** — the exact whole-database delete this task exists to
refuse. `KeyPrefix = "*"` did the same to `ClearAsync` via `"*:*"`, reaching every sibling's namespaced key.
The literal is now glob-escaped (`\ * ? [ ]`), which also fixes a latent read/write disagreement:
`GetFullKey` has always written metacharacters as literals, so the delete side was matching keys the read side
never wrote.

**Step-6 split: 9 of 27 new tests fail against the reintroduced defects** (both reintroduced surgically — a
full revert would have hidden all 27 behind a build error). **Re-derived four times**, because the suite kept
changing: 5 of 13 → 5 of 16 (`verify-conventions` check 5 added the flush-door tests) → 8 of 25 (the inline
security pass added the glob-escape tests) → **9 of 27** (the `code-review` findings added the null-argument
and admin-mode tests). A split expires the moment the suite changes; the first number would have understated
the check by fourteen tests and the fix by an entire defect. Fix-dependent, by name:

- refusal (6): `ClearAsync_WithNoKeyPrefix_RefusesInsteadOfFlushingTheDatabase`,
  `RemoveByPrefixAsync_WithNoKeyPrefixAndEmptyPrefix_RefusesTheSecondDoorToo`,
  `RemoveByPrefixAsync_WithNullPrefix_ReportsNullNotEmptyString`, `ClearAsync_RefusalNamesBothOptOuts`,
  `ClearAsync_RefusalReportsTheDatabaseItWouldHaveEmptied`,
  `WholeDatabaseDeleteException_IsCatchableAsInvalidOperationException`
- escaping (3): `ResolveOwnedKeyPattern_EscapesAStarPrefixThatWouldOtherwiseMatchEverything`,
  `ResolveOwnedKeyPattern_EscapesAStarKeyPrefixSoClearStaysInItsNamespace`,
  `RemoveByPrefixAsync_WithAGlobPrefix_ScansForALiteralNotAWildcard`

The other 18 are **contract pins, not evidence**: three assert the fix did not narrow a working path
(`ClearAsync_WithAKeyPrefix_IsNotRefused`,
`RemoveByPrefixAsync_WithANonEmptyPrefix_IsNotRefusedEvenWithNoKeyPrefix`,
`ClearAsync_StillHonorsCancellationBeforeTheScopeCheck`); four pin the new flush door
(`FlushDatabaseAsync_IsNotReachableThroughICache`, `FlushDatabaseAsync_HonorsCancellation`,
`FlushDatabaseAsync_IsNotItselfRefusedByTheScopeGuard`,
`FlushDatabaseAsync_RequiresAdminMode_WhichSettingsCannotProduce`); five pin the `ResolveOwnedKeyPattern`
decision table on metacharacter-free input; and six pin `EscapeGlob` directly. Every one of the latter survives
a surgical revert (it touches only the two call sites and one expression) and none would compile against a full
revert — pins in both directions, never proof.

### What the close-gate `code-review` changed

It ran at `high` and returned eight findings; five were acted on, one was already fixed, two were bookkeeping.

- **high — the recorded mechanism was wrong.** Verified and corrected everywhere (see § *Re-verified* and the
  root `CLAUDE.md` entry). The narrative had reached five places — § Conventions, `Recent Updates`, the
  exception's XML doc, this file, and a spec scenario — before anyone compared it to the library's behaviour.
- **high — the escape hatch could not open.** `FLUSHDB` is admin-gated; the refusal message sent operators to
  a door that answers with an unrelated exception. The message now names `KeyPrefix` first and states the
  precondition, `FlushDatabaseAsync` documents it, and a test pins both.
- **medium — the test suite was destructive.** Fixed; see § *Flagged* below. This was the worst of the eight.
- **medium — `FlushDatabaseAsync_IsNotItselfRefused` could not fail for its stated reason.** Narrowed to what
  it observes, with the real property pinned separately.
- **medium — the glob-metacharacter door.** Already closed by the inline security pass before the review
  landed; independent confirmation, no action.
- **low — the `SCAN` doc overclaimed** a guarantee StackExchange.Redis does not make (it picks `SCAN` *or*
  `KEYS` by server capability, and `KEYS` is itself admin-gated). Reworded.
- **low — the refusal misreported `RemoveByPrefixAsync(null)`** as `("")`. Fixed and pinned.
- **low — `EPIC.md`'s tally and TASK-206's wording** contradicted this task's own state. Corrected.

It also independently re-derived STORY-053/054's finding arithmetic (421 + 387 = 808, tiling with no gap or
overlap) and confirmed the prefixed paths emit byte-identical patterns to before — the two things most likely to
have broken quietly.

### Judgement calls, and the stricter option in each

- **Refusing beat deleting selectively, because there was nothing to select.** The finding's preferred remedy
  (option 1, "track an owned-key namespace") is not implementable without a prefix: an index needs a key name —
  the very layout change option 2 was rejected for — costs a round-trip per write, and grows without bound
  because Redis expiry does not remove members. Scanning a made-up prefix finds nothing, so `ClearAsync`
  becomes a **silent no-op reporting success**, which is a wrong answer dressed as a right one. The task ranked
  refusal last; it is the only option that is neither destructive nor a lie, and § Conventions already says a
  component that cannot express something refuses rather than dropping it quietly.
- **Fail-fast was legitimate here only because two opt-outs exist and are checked first** — set a `KeyPrefix`,
  or name the destructive operation. Without those a guard is a wall, per the SH-H037 rule.
- **The glob escaping is a fix, not hardening added for its own sake.** `RemoveByPrefixAsync("*")` was already
  a whole-database delete before this task; leaving it would have shipped a guard with a one-character bypass
  and a task record claiming the door was closed. Escaping rather than *refusing* metacharacters was the call:
  a key literally starting with `*` is legitimate to remove, `GetFullKey` already writes such prefixes as
  literals, and refusing them would break a working call to close a hole — the same trap the scope-vs-config
  choice below avoids. The stricter option (reject any prefix containing a metacharacter) is louder but wrong.
- **The guard is scoped to "the pattern is `*`", not to "no KeyPrefix".** The blunter check would have refused
  `RemoveByPrefixAsync("user:")` on an unprefixed cache — a legitimate, bounded call — breaking working
  behaviour in the name of closing a hole. A refusal must not fire on the case it was never about.
- **An empty-string `KeyPrefix` is treated as a namespace, not as absent.** `RedisSettings` distinguishes
  `null` from `""`, keys are written as `":{key}"`, and `":*"` scans exactly those. Collapsing them would have
  been tidier and would have refused a bounded call.
- **`FlushDatabaseAsync` went on the concrete class, not on `ICache`.** Putting it on the interface would let
  any cache-typed consumer empty a database — the shape of the original defect, just renamed.
- **The exception derives from `InvalidOperationException`**, mirroring `WholeTableWriteException` and
  `TenantScopeRequiredException`, so existing `catch (InvalidOperationException)` blocks keep working.

- **`RedisConnectionManager` was left alone.** The tidiest answer to the admin gate would be to enable
  `AllowAdmin` on the multiplexer, or add an `AllowAdmin` property to `RedisSettings`. Rejected: that
  connection manager is shared by every Redis component in the family, and admin mode also unlocks `CONFIG`,
  `SHUTDOWN` and `FLUSHALL` — widening it for four-plus components to make one escape hatch convenient is a
  cross-cutting security decision, not a detail of this fix. Documented as a precondition instead.

### Flagged, not fixed

- **`HybridCache` swallows the refusal** → [[TASK-206]]. Its unqualified `catch when
  (_options.FallbackToL1OnL2Failure)` turns an unprefixed Redis L2 from "flushed the whole database" into
  "silently not cleared". Not a regression this fix introduced — a pre-existing blind spot it newly reaches, and
  the same filter already swallows `OperationCanceledException` (spec'd). Narrowing the filter changes the
  Hybrid fallback contract for *every* failure mode, so it is a decision, not a fix to slip in here.
- **`RemoveByPatternAsync` still deletes with `DEL`, not `UNLINK`.** The task's option 1 mentioned `UNLINK`;
  the acceptance criterion only requires `SCAN` + batching, both of which already held (CR-L040). Changing the
  delete verb is a pre-existing performance question, not part of this defect, and was left alone rather than
  changed on a guess about what `KeyDeleteAsync` maps to.
- **The `FLUSHDB` sweep across all siblings came back empty**, so nothing was filed there. The pattern was
  never copied to `Birko.BackgroundJobs.Redis`, `Birko.MessageQueue.Redis` or the Redis sync stores.
- **The regression suite was itself destructive, and is fixed.** The tests asserting a call is *not* refused
  necessarily run past the guard, so pointed at `localhost:6379` they issued real `SCAN test:*` + `DEL` and
  `SCAN user:*` + `DEL` against database 0 — on any developer box with a local Redis, this suite was deleting
  live `user:*` keys, and `NotThrowAsync<WholeDatabaseDeleteException>` hid every sign. They now target
  TEST-NET-1 (`192.0.2.1`, RFC 5737, guaranteed unroutable). **The evidence was in the runtime all along:**
  36s → 800ms once the connections stopped. A slow "offline" suite is not offline.
- **An `allowAdmin` opt-in on `RedisSettings` was considered and not filed as a task**, because nothing needs
  it yet: the working opt-out is `KeyPrefix`, and a consumer that genuinely wants the flush door already has
  `RawConnectionString`. If a second caller wants it, that is the moment to add the property.
- **SH-H004 / SH-H005 / SH-H007 remain open** in this area, per § Out of scope — and SH-H004/H005 are noted in
  the original finding as *arguably higher impact than this one*. Verify them next.

## Progress log

- step 2 — picked; ranked above TASK-129 (aggregate-view DDL double alias) because this destroys sibling
  components' data on the *default* configuration path and reports success, while TASK-129 destroys nothing
  and fails loudly at view creation
- step 3 — verified: held verbatim; rescoped criterion 1 (an unprefixed cache has no removable key set →
  refusal, not selective delete); pulled in the `RemoveByPrefixAsync("")` sibling door; FLUSHDB sweep across
  all siblings came back negative
- step 4 — layer: local (`Birko.Caching.Redis`; `ICache` in `Birko.Caching` needs no change — the flush door
  is deliberately *not* on the interface)
- step 5 — fix in `Birko.Caching.Redis/RedisCache.cs` (+ new `Exceptions/WholeDatabaseDeleteException.cs`,
  registered in `.projitems`), docs in `Birko.Caching.Redis/CLAUDE.md` and `Birko.Caching/Core/ICache.cs`;
  tests in `Birko.Caching.Redis.Tests/RedisCacheClearScopeTests.cs` (13 new). Blast radius cleared against all
  four suites that compile `Birko.Caching*`: Redis 25/25, Hybrid 39/39, Caching 40/40, SQL.Caching 7/7
- step 6 — reintroduced the defect surgically (two call sites; the exception type and `ResolveOwnedKeyPattern`
  left in place, because a full revert would hide 13 new tests behind a build error): **5 of 13 failed**,
  restored, 25/25 green again. Fix-dependent = `ClearAsync_WithNoKeyPrefix_RefusesInsteadOfFlushingTheDatabase`,
  `RemoveByPrefixAsync_WithNoKeyPrefixAndEmptyPrefix_RefusesTheSecondDoorToo`,
  `ClearAsync_RefusalNamesBothOptOuts`, `ClearAsync_RefusalReportsTheDatabaseItWouldHaveEmptied`,
  `WholeDatabaseDeleteException_IsCatchableAsInvalidOperationException`. Contract pins (**not** evidence) =
  `ClearAsync_WithAKeyPrefix_IsNotRefused`,
  `RemoveByPrefixAsync_WithANonEmptyPrefix_IsNotRefusedEvenWithNoKeyPrefix`,
  `ClearAsync_StillHonorsCancellationBeforeTheScopeCheck`, and the five
  `ResolveOwnedKeyPattern_*` cases (4 theory rows + 1 fact) — those pin a helper the surgical revert does not
  touch, and would not compile against a full revert at all. 3 + 5 = the 8 that passed
- step 7 — respecced `caching`. Requirements changed: *"Redis ClearAsync depends on whether a key prefix is
  configured"* → *"…refuses when it cannot scope itself"* (the title asserted the old behaviour); *"Redis prefix
  removal scans and deletes in batches"* gains the guard + 2 scenarios; *"Redis connection ownership"* reworded
  (the FLUSHDB index now belongs to `FlushDatabaseAsync`); **new** requirement *"FLUSHDB is reachable only
  through an explicitly named door off the ICache surface"*. Unmapped check: 0 of 13 `.cs` under the area's
  four roots unmatched — the new exception file is covered by the existing `**/*.cs` glob, no map change.
  `shaped-by` stamped `[FEATURE-014]` with `shaped-by-derived: false`, because the evidence pass cannot run
  from this aggregator (verified: TASK-113's `pr: 86c8247` is an unknown revision here). Diff review raised
  **one finding** → [[TASK-206]]
- step 8 — merge gate. `verify-conventions` (project-local shadow) raised two real findings **on this change**:
  step-0b register-on-introduce (the second instance of one guard, recorded only in SQL terms — now a
  generalised § Conventions bullet) and check 5 (`FlushDatabaseAsync` new public with no test, and criterion 7
  ticked on construction rather than evidence — now 3 more tests, incl. a reflection assertion that `ICache`
  does not expose it). Check 9 → `Recent Updates` entry added. Build clean with `-warnaserror`, 0 warnings.
  **Step 6 re-derived** because those tests changed the suite: 5 of 16 (was 5 of 13). `security-review` could
  not run (`origin/HEAD` unresolvable — and the production change is in a sibling repo no skill here can diff),
  so the pass ran **inline** per close 5b, and found a **bypass of the new guard**: an unescaped glob
  metacharacter in the prefix (`RemoveByPrefixAsync("*")` → `"**"`) walked straight past the emptiness check
  and matched every key. Fixed in scope (`EscapeGlob`), spec updated with 3 scenarios, step 6 re-derived a
  third time: 8 of 25. `code-review` (high) then returned 8 findings — two **high** (the recorded mechanism was
  wrong: `FLUSHDB` is admin-gated, so the *documented* door threw rather than destroyed, and the escape hatch
  the refusal named could not open; both verified by reflecting SE.Redis 2.8.41 and corrected in five places),
  three **medium** (the suite was issuing real `SCAN`+`DEL` against `localhost:6379`; an assertion that could
  not fail for its stated reason; the glob door, already closed by the inline pass), three **low** (a `SCAN`
  doc overclaim, a misreported null argument, a stale EPIC tally). All acted on; step 6 re-derived a **fourth**
  time: **9 of 27**. Build clean 0 warnings, suite 39/39, runtime 36s → 800ms once the tests stopped connecting
- step 8 — closed `done`. Four repos, production first: `Birko.Caching.Redis@333ba76` (the fix),
  `Birko.Caching@26e6a3f` (the `ICache` obligation), `Birko.Caching.Redis.Tests@1ae2c21` (27 tests), and this
  aggregator commit. `integration: single-branch`, so no branch and no merge step — `done` means on `main`
