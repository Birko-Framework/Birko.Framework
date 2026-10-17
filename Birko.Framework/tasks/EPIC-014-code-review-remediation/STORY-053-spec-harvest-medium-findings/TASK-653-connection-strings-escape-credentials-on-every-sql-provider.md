---
id: TASK-653
parent: STORY-053
feature: null
# status: todo | in-progress | verify (code done, sign-off pending) | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: [SH-M279, SH-M280]
pr: null
github-issue: null
jira-key: null
---

# Connection strings escape credentials on every SQL provider

## Context

Spawned 2026-10-17 from [[TASK-164]] (the settings-configuration-chain triage, which holds SH-M279 and SH-M280), because
a consumer needs it before go-live: **Symbio TASK-928** ("a server database password containing `;` breaks every
connection Symbio opens"). Symbio's production deploy (Symbio TASK-777) generates its database password, and until this
lands its `deploy/godzilla/.env.example` must ask for one without `;`.

Only `PostgreSqlSettings.GetConnectionString` composes through a driver builder
(`NpgsqlConnectionStringBuilder`, CR-L189), which quotes a value containing `;` or `=`. Every other place that puts a
credential into a connection string interpolates it raw. Found by reading on 2026-10-17, **not yet measured**:

| Sink | Composes | Finding |
|---|---|---|
| `Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs:32` | `$"…User ID={UserName};Password={Password};…"` | SH-M280 |
| `Birko.Data.SQL.MySQL/Stores/MySqlSettings.cs:27` | `$"Server={Location};…Password={Password};…"` | SH-M280 |
| `Birko.Data.TimescaleDB/Stores/Settings.cs:54` | `$"Host={Location};…Password={Password};…"` (same driver as PG) | SH-M279 |
| `Birko.Data.SQL.PostgreSQL/Database/Connectors/PostgreSQLConnector.cs:206-218` | `CreateConnection`'s plain-`RemoteSettings` fallback, `string.Format("Host={0};…Password={3};…")` | not raised by the harvest |
| `Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs:206-212` | the same fallback for MySQL | not raised |
| `Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs:92`, `Database/Connectors/SqLiteConnector.cs:226` | `cs += $";Password={Password}"` | not raised |

The `RemoteSettings` fallback is what Symbio actually runs: `AddDataProviders` registers PostgreSQL, MsSql and MySql
with a plain `RemoteSettings`. So Symbio's own option A in TASK-928, registering the provider settings types, fixes
PostgreSQL only. On MsSql and MySql the provider settings interpolate too.

**Why it is worse than a failed connection:** a password `pa;ss` yields `Password=pa;ss;`, cutting the value short and
leaving `ss` as a bare keyword. A value that carries a keyword overrides the settings object without anything saying so:
`Encrypt=False`, `TrustServerCertificate=True`, `SSL Mode=Disable`. The connection then drops TLS while the settings say
`UseSecure`. User names, hosts and database names reach the same sinks.

SqlClient (`SqlConnectionStringBuilder`), MySqlConnector (`MySqlConnectionStringBuilder`), Npgsql and
Microsoft.Data.Sqlite (`SqliteConnectionStringBuilder`) all ship builders that quote correctly.

## Acceptance criteria

- [x] Measured live first (rule 54): a password containing `;` and `=` fails on each sink above, and an injected keyword
      (e.g. `x;Encrypt=False` / `x;SSL Mode=Disable`) is honoured. Red on the current code.
- [x] Every sink composes through its driver's connection-string builder, including the connectors' `RemoteSettings`
      fallbacks; one producer per provider, so the settings path and the fallback cannot drift apart again.
- [x] A password, user name and database name containing `;`, `=`, `'` and `"` round-trip unchanged into the opened
      connection on SQL Server, MySQL, PostgreSQL and TimescaleDB (live suites, `BIRKO_REQUIRE_LIVE`). SQLite ships plain
      `Microsoft.Data.Sqlite` without SQLCipher, so an encrypted open cannot be measured there: its composed string is
      read back through `SqliteConnectionStringBuilder` instead, with `Password` and `Data Source` exact (a `Path`
      containing `;` is interpolated today too).
- [x] An injected keyword in a credential has no effect: the composed builder reads back `Encrypt` / `SslMode` as the
      settings set them.
- [x] Every key the interpolated strings set today survives (timeouts, MARS, `TrustServerCertificate`,
      `Persist Security Info=False`, `SslMode` / `SSL Mode`, `Default Timeout`); the existing settings tests are
      updated to the builder's spelling where it differs, not weakened. Note the Npgsql builder omits keys at their
      default value.
- [x] `Settings.GetId()` (the connector cache key) is unchanged.
- [x] The spec `docs/specs/settings-configuration-chain.md` updated; [[TASK-164]] records SH-M279/M280 as moved here;
      Symbio TASK-928 linked to this task.

## Out of scope

- SH-M281 (MongoDB URI credentials not URL-encoded, a user name with no password dropped) and the other findings of
  [[TASK-164]]: they stay in that triage.
- The connector cache keyed by `GetId()` with the password excluded (a password change needs a restart), as Symbio
  TASK-928 records.
- Symbio's own registration change (provider settings types instead of `RemoteSettings`) and its `.env.example`
  guidance: Symbio-side work in TASK-928.

## Human test plan

N/A — a library contract, asserted by live tests on every provider.

## Implementation plan

_Populated by `/tasks plan TASK-653` — leave empty until then._

## Resolution (2026-10-17)

**Measured live first: 8 red on HEAD, plus the SQLite probe.** Docker containers: SQL Server, MySQL, PostgreSQL and
TimescaleDB. Each test creates a login and a database whose names and password contain `;`, `=`, `'` and `"`.

- **Round trip:** on every server provider the interpolating sink failed with "Format of the initialization string
  does not conform to specification". That covers the MySQL and SQL Server settings, the PostgreSQL and MySQL
  connectors' `RemoteSettings` fallbacks, and TimescaleDB.
- **Injection is worse than the task said:** a password ending in `;Server=evil` or `;Host=evil` **moved the
  connection to the host named in the password** on every server provider. `;Encrypt=False`, `;SslMode=None` and
  `;SSL Mode=Disable` dropped TLS, as the task said.
- **SQLite:** HEAD's string for a path containing `;Mode=Memory` does not parse ("keyword 'b.db;password' is not
  supported").

**Fix: one producer per provider, each composing through the driver's builder.**

| Provider | Producer | Also used by |
|---|---|---|
| SQL Server | `MSSqlSettings.GetConnectionString()` (`SqlConnectionStringBuilder`) | `MSSqlConnector`'s `RemoteSettings` path, which already derived an `MSSqlSettings` (CR-M136) |
| MySQL | `MySqlSettings.ComposeConnectionString` | the `MySQLConnector` fallback |
| PostgreSQL | `PostgreSqlSettings.ComposeConnectionString` | the `PostgreSQLConnector` fallback, and `TimescaleDBSettings` (same driver) |
| SQLite | `SqLiteConnector.ComposeConnectionString` | `SqLiteSettings` and the connector's plain-settings path |

- **Fallback key sets are unchanged:** no timeouts, the default port when `Port <= 0`, and the secure mode. The
  settings paths keep every key they set before. `Every_key_the_interpolated_string_set_survives` checks this per
  provider: MARS, `TrustServerCertificate`, `Persist Security Info=False`, `Encrypt`, `Connect Timeout`, `SSL Mode`,
  `Timeout` / `Command Timeout` and `Default Timeout`.
- **`GetId()` is untouched,** and the SQL Server test pins it.
- **Builder spellings:** the builders write their own key names (`Data Source`, `Multiple Active Result Sets`,
  `Trust Server Certificate`, `Connect Timeout`, MySQL's `SSL Mode`). Five existing tests pinned the old spellings and
  are updated (see the regression note).
- **Measured, contrary to CR-L189's note and this task's:** Npgsql's builder does **not** omit keys at their defaults.
  `Port=5432;Timeout=15;Command Timeout=30` are emitted. The spec scenario saying otherwise is corrected, and so is the
  code remark.

**Tests:**
- `Task653ConnectionStringEscapingLiveTests`, in the SQL Server, MySQL, PostgreSQL and TimescaleDB suites: the live
  round trip on both paths, the injected keyword read back, and the key set.
- `Task653ConnectionStringEscapingTests` (SQLite): the path and password read back exact, `Mode` unchanged.

**Elsewhere:**
- Spec `settings-configuration-chain.md`: the four "raw interpolation" requirements are replaced by one requirement
  with a producer table and measured scenarios. The SQLite requirement is updated.
- TASK-164 already records SH-M279/M280 as moved here.
- Symbio TASK-928 already links this task (lines 37-48). Symbio still needs its own option A for the typed settings;
  that is its side of TASK-928. With this fix, plain `RemoteSettings` is safe too.

Regression: all 25 importers of the five provider projitems pass live. The first run failed 5 existing tests that
pinned the old spellings (`Server=tcp:`, `MultipleActiveResultSets=`, `TrustServerCertificate=`, `Connection Timeout=`,
`SslMode=Required`). They are updated to the builders' spellings of the same keys and values, not weakened. My
earlier grep for pins missed them.
