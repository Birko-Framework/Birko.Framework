---
id: TASK-287
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-31
depends-on: [TASK-285, TASK-286]
blocks: []
findings: []
pr: 708b147 (Birko.Data.SQL) + 270cc3c (Birko.Data.SQL.SqLite.Tests)
github-issue: null
jira-key: null
---

# The count path swallows TASK-286's escape annotation, so the instrument is blind where it is needed

## Context

TASK-285 and TASK-286 were both raised by **Symbio TASK-602** (an intermittent 500 on a fresh database)
and both are correct on their own. Together they leave a hole, and it is precisely over the case the
instrument was built to catch.

- **TASK-285** made a `COUNT` on a missing table return `0` instead of throwing.
- **TASK-286** made `EnsureSchemaAndReport` annotate its exception when *this connector already created*
  the table being reported missing — the anomaly — and say `NO recorded CREATE TABLE` otherwise.

The annotation travels **on the thrown exception**. On the count path there is no longer a thrown
exception to travel on: `AbstractConnector_SelectCount.cs:64` and its async twin `:68` catch
`when (IsMissingTableExceptionChain(ex))` and `return 0`. That catch walks the **chain** precisely because
`EnsureSchemaAndReport` already ran and already rethrew — its own comment says so. So the annotation is
produced and then discarded with the exception carrying it.

**Measured 2026-08-31 against a live Symbio API**, both halves in the same condition, minutes apart. The
condition was produced deliberately: touch the route so the store initialises and the table exists, then
`DROP TABLE "LeaveRequests"` under the running process.

| statement shape | HTTP | annotation reaches the log |
|---|---|---|
| **COUNT** (`GET /api/hr/leave`) | **200**, `totalCount: 0` | **NO — zero lines** |
| **write** (`POST /api/hr/leave`) | 500 | **yes** |

⚠ **Both occurrences Symbio TASK-602 recorded were counts** — `SELECT count(*) as count FROM
"LeaveRequests"` and `… FROM "CustomerAccounts"`. So the instrument cannot see the one shape that has ever
actually exhibited the anomaly in the wild.

⚠ **The consequence is a false negative that reads as good news.** Nineteen instrumented Symbio bring-ups
since have logged **0** escapes against **4,397** benign first-touch errors. On the write path that
silence is real; on the count path `0` is what a blind instrument reports whether the condition happened
0 times or 19. Symbio TASK-602's "the silence is now MEASURED silence" has been corrected there.

⚠ **And on one shape it is now a silently wrong answer, not merely an unobserved one.** Symbio's second
occurrence was a `COUNT` **inside a write** (`POST /api/customer-accounts`, a duplicate-email pre-check).
Under TASK-285 that count answers `0`, so the same condition now yields a wrong pre-check result instead
of a 500.

## The design, and the option deliberately rejected

**Do this: record and raise, keep returning `0`.** The discriminator already exists — TASK-286 writes
`NO recorded CREATE TABLE` for ordinary lazy first-touch (~245 per Symbio bring-up, pure noise) and the
`already created it` text only for the anomaly. So the count catch can stay silent for the benign case and
surface the anomalous one through the connector's existing failure-log/event convention
(`SchemaEnsureFailureLog`, `OnIndexCreationFailed` — mirror it, do not invent a second shape).

⚠ **REJECTED: rethrowing on the anomalous case.** Tempting, and it would be loud for free — the table is
not genuinely missing there, so `0` is a wrong answer and a throw is arguably more correct. It is rejected
because it silently re-opens what TASK-285 closed: at roughly 1-in-5 bring-ups it breaks Symbio's seed
again and turns a real defect back into something read as infrastructure flakiness. If anyone wants that
behaviour it is a **decision with a consequence**, taken deliberately and written down — not a side effect
of adding an instrument.

⚠ **A raised event alone is not enough for Symbio and that is measured**: Symbio subscribes to no connector
event and surfaces connector diagnostics only through **boot-time** checks, which cannot see a runtime
escape (Symbio TASK-602, Round 4). So the host has to subscribe — that half is a Symbio task, and this one
is not finished until something actually writes the line.

## Acceptance criteria

- [x] The anomalous case is recorded and observable from the count path; the benign case stays silent.
      ⚠ Discriminate on TASK-286's annotation, never on "the table was missing" — the benign case is also
      a missing table and is ~245× more common.
- [x] `return 0` is unchanged for both cases. This task adds observation, not behaviour.
- [x] **Both** count files — `AbstractConnector_SelectCount.cs` and `AbstractAsyncConnector_SelectCount.cs`.
      They are separate code paths; shipping one is how half a fix looks green.
- [x] Mutation-proven in the direction that matters: with the recording removed, a forced anomaly on the
      **count** path goes unobserved while the write path still reports. A test that only exercises the
      write path passes against this defect unchanged — that is exactly how it was missed.
- [x] The benign path is asserted too: a genuine first-touch `COUNT` on a cold table records **nothing**.
      Without it the guard could pass by recording everything, which at 245 per bring-up is no signal.

## Out of scope

- **Why a table is created and then not visible to a later statement.** That is Symbio TASK-602's question
  and remains unanswered; this only makes it observable where it actually occurs.
- **Softening `EnsureSchemaAndReport`.** It throws because TASK-277 measured writes being silently
  discarded. Writes must keep reporting. *(Held: the write path still throws, and there is a test for it.)*
- **The host-side subscription** that turns the record into a log line — Symbio's side. **Still open, and
  this task is not observable in production until it lands.** The framework now records the escape on
  `AbstractConnector.SchemaEscapes` and raises `OnSchemaEscapeDetected`; nothing in Symbio subscribes.
- **TASK-627 (Symbio)** — after a genuine disappearance nothing ever recreates the table. Same area, and a
  different defect: measured, five consecutive writes 500-ed and only a process restart recovered.
  *Now owned by [[TASK-288]], which fixed the framework half.*

## Human test plan

- [x] N/A — the verdict is whether a line is recorded under a deliberately forced condition, which an
      assertion measures.

## Outcome (2026-08-31)

Landed as `708b147` (`Birko.Data.SQL`) + `270cc3c` (`Birko.Data.SQL.SqLite.Tests`).
`Birko.Data.SQL.SqLite.Tests` **271 passed, 0 failed, 0 skipped** with `BIRKO_REQUIRE_LIVE` set
(261 → 271), `Birko.Data.SQL.Tests` **655**, and the adjacent SQLite-backed suites green.

The channel is `SchemaEscape` / `AbstractConnector.SchemaEscapes` / `OnSchemaEscapeDetected`, on the
existing `SchemaEnsureFailureLog` — keyed, transition-fired, locked, ordered, exactly as the index channel
is, because the count path runs per request against a process-wide cached connector and that is precisely
how TASK-204's append-only list grew one entry per HTTP request.

Four mutations, disjoint, each isolating one claim:

| mutation | red | which |
|---|---|---|
| remove both `RecordSchemaEscape` calls | **3** | the three count-path observation tests — **the write-path test stays green**, which is the whole point |
| remove only the sync call | 2 | the async test passes, so half a fix does not look green |
| collapse the chain walk to the outermost message | 1 | only the synthetic twice-wrapped case |
| discriminate on "the table was missing" | 2 | the two benign first-touch tests |

Three things worth carrying:

- **⚠ The chain walk is defensive, not witnessed, and it is labelled that way on the method.** Measured:
  on both live SQLite count paths the annotation sits at depth **0**, because nothing between
  `EnsureSchemaAndReport` and the catch wraps again. So the loop is pinned only by a synthetic test. Kept
  because the depth is a property of the call stack (`InitException` is reached from eleven sites) rather
  than a promise, and the failure mode of guessing wrong is a guard that runs and never matches — which
  has already happened once in this area.
- **The marker has one producer.** `AnomalousEscapeMarker` is interpolated into `DescribeSchemaEscape`
  rather than spelled out twice, so the text that is written and the text that is matched cannot drift.
- **No `Clear` counterpart**, the one place this channel departs from the index one. An unbuildable index
  is a current condition an operator repairs; an escape is a past event at a timestamp that heals within
  milliseconds, so clearing on the next successful count would delete the record before any reader saw it.
  `SchemaEnsureFailureLog`'s "exactly two callers and no more are expected" remark was corrected in the
  same commit rather than left stale.

### ⚠ Superseded in part by [[TASK-288]], one commit later

TASK-288 needed the *same* detection to bump a schema generation so a store whose table vanished stops
trusting its remembered initialization — and record-the-escape and invalidate-the-store are one event, so
they cannot be wired separately. The detection therefore moved one layer earlier, into
`EnsureSchemaAndReport`, and **the two count-path call sites this task added are gone**. Nothing about the
observable behaviour changed for the count path; what changed is that the channel now also covers the
write path, and one assertion in this task's own suite was **inverted rather than deleted**, with a comment
recording where the line moved. The comments in both count files still explain the reasoning, then say
where it went.

## Implementation plan

_Not populated — the design was specified in full at filing time and implemented directly._
