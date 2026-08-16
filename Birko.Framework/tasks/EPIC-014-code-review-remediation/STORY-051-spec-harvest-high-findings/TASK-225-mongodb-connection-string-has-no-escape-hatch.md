---
id: TASK-225
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-223]
pr: null
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

- [ ] A consumer can supply a complete MongoDB connection string without subclassing `Settings`
- [ ] The composed path is unchanged when the escape hatch is unset — existing consumers see no difference
- [ ] Whatever shape is chosen matches `RedisSettings`' precedent, or the divergence is justified in the
      task and in `Birko.Data.MongoDB/CLAUDE.md`
- [ ] A non-gated test pins both paths, including that credentials in the raw string are not re-appended
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- Cosmos's `ConnectionMode` ([[TASK-223]]) — a different mechanism for a related need.
- Auditing every settings class for every missing driver option. This is about the escape hatch, not
  about enumerating knobs.

## Human test plan

- [ ] N/A — fully covered by automated tests. Connection-string composition is pure string building and
      both branches are asserted; a human adds nothing a test does not.

## Implementation plan

_Populated by `/tasks plan TASK-225` — leave empty until then._
