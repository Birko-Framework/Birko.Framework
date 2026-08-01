---
id: TASK-051
parent: STORY-039
feature: FEATURE-016
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# FIX: MSSqlStore.SetSettings drops connection fields (lossy)

## Context

Discovered while backporting the cross-provider store factory (TASK-042): the **sync**
`MSSqlStore<T>.SetSettings(RemoteSettings)` rebuilds a `PasswordSettings` keeping only
Location/Name/Password, **dropping UserName / Port / MultipleActiveResultSets / TrustServerCertificate /
UseSecure**. The store's connector is then built from that narrowed `PasswordSettings`, so its
connection string omits the user id and SQL-Server-specific options → authentication fails and MARS /
TrustServerCertificate are silently ignored.

`AsyncMSSqlStore` is already correct (`base.SetSettings((ISettings)settings)`), and both the **MySQL**
and **PostgreSQL** sync stores already use the correct pattern — full-settings pass-through plus a
`SetSettings(PasswordSettings)` override that redirects a `RemoteSettings`. `MSSqlStore` is the only
outlier. Fix = make it match its peers.

## Acceptance criteria

- [x] `MSSqlStore<T>.SetSettings(RemoteSettings)` passes the **full** settings through
      (`base.SetSettings((ISettings)settings)`), no longer narrowing to `PasswordSettings`.
- [x] `MSSqlStore<T>` has the `override void SetSettings(PasswordSettings)` redirect. — it already existed but
      redirected *into* the lossy `RemoteSettings` overload; fixing that method makes both entry points full-fidelity.
- [x] Regression test: after `SetSettings` with a full `MSSqlSettings`, the store's connector retains
      `User ID` and `MultipleActiveResultSets=True`. — `MSSqlStore_SetSettings_retains_full_connection_fields` (green).
- [x] `Recent Updates` entry added.

## Out of scope

- MySQL / PostgreSQL sync stores — already correct, no change.
- Any live-DB behaviour (covered by TASK-042's env-gated round-trip).

## Human test plan

- [ ] N/A — fully covered by the automated regression test (connection-string retention is observable offline).

## Implementation plan

_Populated by `/tasks plan TASK-051` — leave empty until then._
