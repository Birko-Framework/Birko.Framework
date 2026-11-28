---
id: TASK-1023
parent: EPIC-019
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: [TASK-1013]
blocks: []
related: [TASK-1013]
findings: []
pr: null
github-issue: null
jira-key: null
---

# OAuth factory-store tables have no index on the values they are looked up by

## Context

Found at [[TASK-1013]] on 2026-11-28; the user chose a follow-up task over doing it in TASK-1013.

The SQLite OAuth package indexed `oauth_clients.ClientId` and the refresh-token hash (`[IndexedField]`, Birko.Data.SQL).
TASK-1013's entities live in the core OAuth package, which does not reference Birko.Data.SQL, so they carry
DataAnnotations only, and a table the factory stores create gets no index. Existing tables (written by the SQLite package)
keep theirs. Lookups by client id, token hash, code, device and user code, and consent (user, client) then scan the
table on SQL (and on MongoDB, which also creates indexes from declarations where the store supports it).

## Acceptance criteria

- [ ] A new table (and collection, where the backend creates indexes from a declaration) has an index on every value the
  OAuth stores look up by, without the core OAuth package depending on Birko.Data.SQL (for example a registration from
  the OAuth package, as `SqLiteOAuthTables` registers its tables, or a backend-neutral index declaration if one exists).
- [ ] A test shows the index exists on a fresh database for each SQL provider (and MongoDB if covered).
- [ ] An existing database written by the SQLite package is still read and written (the TASK-1013 legacy test passes).

## Human test plan

N/A — fully covered by automated tests.
