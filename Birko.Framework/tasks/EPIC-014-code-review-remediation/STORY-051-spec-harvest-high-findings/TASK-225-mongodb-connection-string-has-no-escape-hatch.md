---
id: TASK-225
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-223]
pr: [Birko.Data.MongoDB@2dcef00, Birko.Data.MongoDB.Tests@93ccf6f]
github-issue: null
jira-key: null
findings: []
---

# MongoDB's connection string is composed with no escape hatch — no driver option can be set

## Context

Found by [[TASK-223]]'s audit of *"do the other providers have a Gateway equivalent?"*. They do not —
Gateway is Cosmos-specific — but the audit surfaced the same **shape** of gap in MongoDB.

`Birko.Data.MongoDB.Stores.Settings.GetConnectionString()` composes the URI from `Location`, `Port`,
credentials and `Name`, then appends a **fixed** set of query parameters: `authSource`, `replicaSet`
(when set), `tls` (when `UseSecure`), `retryWrites=true`, `retryReads=true`. There is no way to add any
other option, and no `RawConnectionString` override.

**Redis already solved exactly this.** `Birko.Redis.RedisSettings.RawConnectionString` is returned
verbatim when set, and `Birko.Caching.Redis` relies on it as the documented opt-in for `allowAdmin=true`
(SH-H006). MongoDB has no equivalent; neither does InfluxDB or TimescaleDB, the other two that compose
their own string.

**This is not hypothetical — it was hit three times inside one session.** Every live-MongoDB probe in
[[TASK-214]] and [[TASK-219]] had to subclass `Settings` and override `GetConnectionString()` just to set
`serverSelectionTimeoutMS`. A consumer who needs `maxPoolSize`, `appName`, `connectTimeoutMS`,
`readPreference`, `w`/`journal` write concern, `directConnection`, or a SOCKS proxy (`proxyHost` /
`proxyPort`, the closest thing MongoDB has to Cosmos's Gateway) has the same problem and the same
workaround: inherit from the settings class.

Lower priority than [[TASK-223]] because a workaround exists and is not obscure — but subclassing a
settings type to set a timeout is not a design, it is a gap with a habit grown over it.

## Approach

1. Add `RawConnectionString` to `Birko.Data.MongoDB.Stores.Settings`, returned verbatim from
   `GetConnectionString()` when set — **mirroring `RedisSettings` exactly**, so the family has one answer
   rather than two. Reuse the doc wording; a consumer should not have to learn two rules.
2. Consider the weaker, composable alternative instead or as well: an `AdditionalOptions` dictionary
   appended to the query string. **Decide between them — do not ship both.** Redis chose verbatim;
   matching it is the default unless there is a reason not to.
3. Apply the same to InfluxDB and TimescaleDB **only if measured to need it** — per the standing rule,
   available to all, wired where a gap is demonstrated.

## Acceptance criteria

- [x] A consumer can supply a complete MongoDB connection string without subclassing `Settings` —
      `RawConnectionString`, returned verbatim
- [x] The composed path is unchanged when the escape hatch is unset — existing consumers see no
      difference. Pinned by `The_composed_form_is_unchanged_when_the_hatch_is_unset`
- [x] Whatever shape is chosen matches `RedisSettings`' precedent, or the divergence is justified in the
      task and in `Birko.Data.MongoDB/CLAUDE.md`. **Matches exactly**, including the non-empty rule Redis
      had to correct in CR-L331 — so no divergence needed justifying
- [x] A non-gated test pins both paths, including that credentials in the raw string are not re-appended
      — `A_raw_string_is_returned_verbatim` asserts byte equality against a raw string carrying
      credentials, a replica set and four extra options, so nothing can be appended to it
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6

## Out of scope

- Cosmos's `ConnectionMode` ([[TASK-223]]) — a different mechanism for a related need.
- Auditing every settings class for every missing driver option. This is about the escape hatch, not
  about enumerating knobs.

## Human test plan

- [ ] N/A — fully covered by automated tests. Connection-string composition is pure string building and
      both branches are asserted; a human adds nothing a test does not.

## Outcome

**What was broken.** `GetConnectionString()` composed the URI and appended a fixed set of query
parameters, with no way to add another — so `maxPoolSize`, `appName`, `connectTimeoutMS`,
`serverSelectionTimeoutMS`, `readPreference`, write concern, `directConnection` and the SOCKS
`proxyHost`/`proxyPort` pair were all unreachable. The only workaround was to subclass `Settings`, which
this framework's own live probes did three times in one session ([[TASK-214]], [[TASK-219]]) just to set
a timeout.

**The fix.** `RawConnectionString`, returned verbatim when non-empty — a deliberate copy of
`Birko.Redis.RedisSettings.RawConnectionString`, not a new idea.

**Judgement calls.**

- **Copied Redis rather than designing.** The task offered an `AdditionalOptions` dictionary as the
  composable alternative. It is arguably nicer, and it would have given the family two different answers
  to one question — a consumer would have to learn which provider takes which. Matching the precedent
  was worth more than the marginally better shape.
- **Inherited Redis's CR-L331 correction deliberately.** Only a *non-empty* value overrides; an explicit
  `""` falls through. Redis shipped the naive version first and had to fix it; copying the fixed version
  is the entire benefit of having a precedent, and there is a test for that case specifically.
- **`LoadFrom` carries it.** The quiet failure mode if it did not: a cloned settings object loses the
  whole connection configuration and falls back to a composed string that is *perfectly valid* — so it
  connects to the wrong place with no error. Same class as [[TASK-223]]'s `ConnectionMode`, one task
  earlier, which is why it was checked here without being prompted.
- **InfluxDB and TimescaleDB were NOT given the same hatch.** They compose their own strings too and the
  Approach listed them, but no gap has been demonstrated for either — the standing rule is available to
  all, wired where measured. Recorded as considered and declined rather than silently skipped.

**Flagged, not fixed.**

- InfluxDB / TimescaleDB, as above. One line each if a need appears.

## Implementation plan

_Populated by `/tasks plan TASK-225` — leave empty until then._

## Progress log

- step 2 — picked; user-directed, and last in the queue by its own P2: a workaround exists (subclass Settings). Option 1 taken — mirror RedisSettings.RawConnectionString exactly, so the family has one answer rather than two.
- step 3 — verified. `GetConnectionString()` emits exactly five query parameters and no escape hatch exists; `Birko.Redis.RedisSettings` already solves the identical problem, so the shape did not need designing. The "three times in one session" claim was re-checked against this session's own probes rather than asserted.
- step 4 — layer: local to Birko.Data.MongoDB.
- step 5 — fix in Birko.Data.MongoDB/Stores/Settings.cs; tests in Birko.Data.MongoDB.Tests/Stores/MongoRawConnectionStringTests.cs (new, 6, non-gated).
- step 6 — revert (drop the early return): 2 of 91 failed = A_raw_string_is_returned_verbatim and LoadFrom_carries_the_raw_string. Contract pins, passing either way: The_composed_form_is_unchanged_when_the_hatch_is_unset and both An_unset_or_empty_hatch_falls_through_to_the_composed_form cases (they assert the UNCHANGED path, so a build with no hatch at all satisfies them — named as pins, not evidence), plus A_store_built_from_raw_settings_uses_it, which passes either way because the composed string is also valid. Fixed state: 91/91, and 7 dependent MongoDB suites green (86 + 54 + 5 + 6 + 8 + 7 + 12).
- step 7 — no spec change. Connection-string composition is configuration surface; no area's globs cover Stores/Settings.cs, and settings-configuration-chain documents the Settings *hierarchy*, not per-provider connection strings. Recorded rather than skipped silently.
