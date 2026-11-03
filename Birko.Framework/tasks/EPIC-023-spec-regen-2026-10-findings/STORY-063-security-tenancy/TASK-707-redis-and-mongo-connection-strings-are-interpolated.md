---
id: TASK-707
parent: STORY-063
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-02]
pr: null
github-issue: null
jira-key: null
---

# Redis and MongoDB connection strings interpolate credentials unescaped

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. TASK-653 moved the SQL providers onto their drivers' connection-string builders. Redis still appends option values unescaped, so `Password = "x,ssl=False"` becomes `password=x,ssl=False` and adds an option; MongoDB does not URL-encode user name, password or database, so `@`, `/` or `?` break or redirect the URI. Same class of injection TASK-653 fixed.

Evidence: Birko.Redis/RedisSettings.cs:53-81; Birko.Data.MongoDB/Stores/Settings.cs:36-48.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-03, before the fix)

Both claims reproduced, each wider than the harvest said. Parsed with each driver's own parser, then live.

| Setting | Value | What the driver read |
|---|---|---|
| Redis `Password` | `x,allowAdmin=true` | password `x`, `AllowAdmin = True` |
| Redis `UserName` | `u,ssl=True` | user `u`, `Ssl = True` |
| Redis `Name` | `n,abortConnect=false` | client name `n`, `AbortOnConnectFail = False` |
| Mongo `Password` | `p@ss/w?rd` | password `p`, server `ss:27017`, database `w` |
| Mongo `Password` | `pw@evil:27017/x` | `MongoConfigurationException` (invalid URI) |
| Mongo `UserName` | `s:vc` | `MongoConfigurationException` |
| Mongo `Password` | `pw%41` | password `pwA` |
| Mongo `AuthDatabase` | `admin&directConnection=true` | `DirectConnection = True` |
| Mongo `ReplicaSet` | `rs0&tls=false` with `UseSecure` | `MongoConfigurationException` (tls set twice) |
| Mongo `Name` | `shop?directConnection=true` | `MongoConfigurationException` |

StackExchange.Redis has no escape for `,`: its own `ConfigurationOptions.ToString()` round-trips
`Password = "x,allowAdmin=true"` to the same `x` plus `allowAdmin`. So no string can be fixed; the connection has
to be made from an options object (rule 34: the API's shape, not a validator).

## Implementation plan

- Redis: new `RedisSettings.GetConfigurationOptions()` sets each value as a property (raw string: `Parse`);
  `RedisConnectionManager(RedisSettings)` connects with a clone of it. `GetConnectionString()` refuses a `,` in the
  password, user or client name with `ArgumentException` (rule 41) instead of emitting a different configuration.
- MongoDB: `Uri.EscapeDataString` on user name, password, database, `authSource` and `replicaSet`, as the MongoDB URI
  format requires. `Location` stays as written: it may be a host list (`h1,h2`), which is configuration, not a
  credential.

## Built (2026-11-03)

`tests/Birko.Redis.Tests/Task707RedisCredentialTests.cs` (8) and
`tests/Birko.Data.MongoDB.Tests/Stores/Task707MongoCredentialTests.cs` (8). Mongo: 8 of 8 failed before the fix.
Redis: the options tests did not compile before it, and two mutations (password cut at the comma; the refusal
removed) were each caught. Measured live (throwaway `redis:7 --requirepass 'x,allowAdmin=true'`,
`mongo:7` with root `s:v@c` / `p@ss/w?rd`): `RedisConnectionManager` answered PING, where the old string form got
`WRONGPASS`; `MongoDBClient` answered `ping` `{ ok: 1 }`. Spec `settings-configuration-chain.md` updated (the two
"not escaped" scenarios replaced; scoped, `generated-at` left as it was).
