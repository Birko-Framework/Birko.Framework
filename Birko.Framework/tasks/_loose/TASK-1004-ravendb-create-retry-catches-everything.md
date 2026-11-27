---
id: TASK-1004
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-996, TASK-998, TASK-1005]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB's database-create retry retries any exception for 30 s

## Context

Found at the 2026-11-27 `/specs regen` of `backend-store-implementations`; the user asked for it to be filed.

[[TASK-996]] made `RavenDatabaseBootstrap.EnsureReady` retry the database create, so two stores starting together on a
new database both start. The retry catches every exception:

```csharp
catch (Exception) when (DateTime.UtcNow < until)   // Birko.Data.RavenDB/Stores/RavenDatabaseBootstrap.cs:42
```

So a create that can never succeed is retried for `ConcurrentCreateWait` (30 s) before it surfaces. Examples: no
permission to create databases, a bad certificate, an unreachable server, a licence limit.

Rule 66 says a retry re-runs a write only when the server says it applied nothing. This one retries without asking what
failed. [[TASK-998]]'s `RavenQueryRetry` names its two exceptions; the bootstrap should be as specific.

## Acceptance criteria

- [ ] Measure which exception(s) the losing create throws when two stores race on a new database (the TASK-996 case), on
  the RavenDB version CI runs.
- [ ] Only those are retried; any other exception from the create surfaces at once.
- [ ] Red first: a create refused for another reason takes the full 30 s today. For example an authorization failure,
  or a stubbed maintenance executor.
- [ ] The TASK-996 race test and the live RavenDB suites still pass.

## Out of scope

- The blocking wait itself, which is [[TASK-1005]]. Doing the two together is fine.

## Human test plan

N/A — fully covered by automated tests.
