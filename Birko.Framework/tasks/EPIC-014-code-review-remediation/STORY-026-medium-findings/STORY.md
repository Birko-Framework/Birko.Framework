---
id: STORY-026
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: medium
finding-count: 275
finding-ids: CR-M001 …
---

# Medium findings

## Progress

**87 / 275 triaged** (as of 2026-07-09). Verify-first paid off repeatedly: several test-gap findings
were already resolved by test projects created since the audit, and CR-M006 / CR-M033 / CR-M053 were false positives.

### Batch 12 — CosmosDB cluster CR-M084 … CR-M087 (4 closed)

Birko.Data.CosmosDB + Birko.Data.CosmosDB.Views. All bugs verified via pure-logic unit tests (no live emulator).

- **M084** `CosmosAggregationHelper` emitted dotted `c.Field` identifiers by raw interpolation → bracket-quoted via a `FieldRef` helper (`c["Field"]`, embedded quotes/backslashes escaped) in SELECT/aggregate/GROUP BY.
- **M085** `CosmosDBIndexManager` stored single-field indexes as `/name/?` but Exists/Drop/GetInfo compared against the raw name (never matched) and Drop pushed the raw name into ExcludedPaths unconditionally → extracted `NormalizeIncludedPath`/`NormalizeFieldPath`/`IncludedPathMatches`/`FieldPathMatches`/`PolicyContainsIndex`/`RemoveIncludedIndex`; Create/Exists/Drop/GetInfo now agree, and Drop only excludes the path it actually removed.
- **M086** `CosmosFilterTranslator.TranslateValue` fell back to raw `ToString()` for DateTime (culture-dependent, unquoted, invalid) and enums (unquoted member name) → DateTime/DateTimeOffset now ISO-8601 quoted, enums emit numeric value.
- **M087** (test-gap) Birko.Data.CosmosDB.Views.Tests already existed; augmented with TranslateValue coverage (doubles as M086's tests).
- Suites green: CosmosDB.Tests 43 (+11), CosmosDB.Views.Tests 7 (+5). Remaining Data.* backend medium findings (CR-M088 …) still to triage.

**The entire Communication cluster (CR-M036 … CR-M075, 40 findings across Bluetooth, Camera, GraphQL,
gRPC, Hardware, IR, Modbus, Network, NFC, OAuth, REST, REST.Server, SOAP, SSE, WebSocket) is closed.**

### Batch 11 — Core foundation CR-M076 … CR-M083 (8 closed)

Configuration, Contracts, CQRS, Data.Composition, Data.Core.

- **3 confirmed bugs fixed:** **M076** `Settings.LoadFrom(ISettings)` hard-cast → type-guard (foreign
  ISettings no longer throws InvalidCastException); **M078** `RetryPolicy.GetDelay` `(long)Math.Pow`
  overflow to a negative TimeSpan at high attempts → compute in double + saturate at MaxDelay before
  cast; **M082** `AbstractModel.CopyTo()` / `AbstractLogModel.CopyTo()` returned `clone!` (null) with no
  arg → guard-clause `return this` (honors non-null ICopyable<T>).
- **Already resolved (verify-first):** **M077** Birko.Configuration.Tests and **M080** Birko.CQRS.Tests
  existed (created since the audit); M080's covariant-dispatch regression was already present. Configuration.Tests
  augmented with GetId + LoadFrom(ISettings) tests.
- **New test projects (3):** **Birko.Contracts.Tests** (10 — M079: RetryPolicy incl. the M078 overflow
  boundary + jitter window), **Birko.Data.Core.Tests** (17 — M083: ExpressionParameterReplacer, filters,
  CopyTo null-handling, ViewModel PropertyChanged), **Birko.Data.Composition.Tests** (8 — M081:
  StoreWrapperBuilder.Build<T> branch/gating/ordering + EventSourcing ctor arity, over the InMemory store).
  Registered in .slnx + .code-workspace.
- Suites green: Contracts 10, Configuration 11, Data.Core 17, Composition 8 (CQRS unchanged). The Data.*
  storage-backend medium findings (CR-M084 …) remain.

### Batch 10 — WebSocket CR-M073 … CR-M075 (3 closed; completes Communication)

- **M073** `WebSocketPort` Write/Open/Close/ReadWorker used `.Wait()`/`.Result` → now
  `ConfigureAwait(false).GetAwaiter().GetResult()` (original exception propagates, no context deadlock).
- **M074** `WebSocketAuthenticationService` had a `Dispose()` but not `: IDisposable` (DI never disposed
  it → leaked `ReaderWriterLockSlim`) → implements the interface.
- **M075** WebSocket.Tests → 33: server start/stop/**restart** lifecycle (real loopback HttpListener on
  a free port) + auth reject/allow + the IDisposable regression.

### Batch 9 — SSE cluster CR-M067 … CR-M072 (6 closed; all bugs)

- **M067** rate-limit `_connectionHistory` grew unbounded → prune + evict fully-aged-out keys each request.
- **M068** `SseResponse.Denied` dropped its reason → added `Reason` property, assigned in `Denied`.
- **M069** query-token parsing broke on leading `?` and `=` in value → `TrimStart('?')` + `Split('=', 2)`.
- **M070** `SseClient.ConnectAsync` leaked prior CTS/task → tears down the prior session first.
- **M071** `SseClient` was a no-I/O stub → implemented real HTTP SSE streaming (injectable HttpClient,
  ResponseHeadersRead, line read → `ProcessEventLine`, reconnect via Last-Event-ID).
- **M072** `CreateComment` emitted `data: : text` → distinct `Comment` property → real `: text` line.
- SSE.Tests → 19 (mock-handler streaming test proves the client now actually receives events).

### Batch 8 — REST / REST.Server / SOAP CR-M059 … CR-M066 (8 closed)

- **REST (M059/M060/M061)** — `Credentials` now backed by the client's `HttpClientHandler` (was a
  dead auto-property); sync overloads documented as convenience shims (prefer `*Async`); CLAUDE.md
  rewritten to the real thin `RestClient` API (the fictional `AsyncRestClient`/`RestRequest`/
  `GetData<T>`/`RestException`/retry/cache surface removed).
- **REST.Server (M062/M063)** — `RestAuthenticationService : IDisposable` (had a `Dispose()` but
  didn't implement the interface → lock leak). REST.Server.Tests exists + IDisposable regression.
- **SOAP (M064/M065/M066)** — client cache → `ConcurrentDictionary`+`GetOrAdd` (was Dictionary
  check-then-act); `SoapServer` gained a synchronous `Stop()` core so `Dispose` no longer blocks on
  `StopAsync().GetAwaiter().GetResult()`. SOAP.Tests exists + cache/server regressions.
- Suites green: REST 19, REST.Server 7, SOAP 7.

### Batch 7 — Communication test-gaps + NFC polling CR-M051/M056/M057/M058 (4 closed)

- **NFC polling (M056)** — added an `INfcTransport.PollingError` event; all three transports
  (Serial/Http/Hid) now wrap the poll loop in try/catch (surface non-cancellation faults + stop),
  track the poll Task, and `StopPollingAsync` awaits it. Regression tests fire `PollingError` on a
  faulting transport.
- **NFC tests (M057)** — NfcReaderPort pipeline/lifecycle, SerialNfcTransport ParseResponse/DetectTagType
  (via reflection), HttpNfcTransport endpoints (mocked handler). NFC.Tests → 121.
- **IR tests (M051)** — SamsungAcProfile, InfraredPort (Write %4, HandleReceivedTiming fallback),
  Http/SerialIrTransport; RC5/RawProtocol already covered. IR.Tests → 102.
- **OAuth tests (M058)** — PollDeviceTokenAsync (pending/slow_down/expired/timeout), real refresh-token
  grant + fallback, ExchangeCodeAsync confidential branch, real 401-resend. OAuth.Tests → 54.

### Batch 6 — Communication ports/protocols CR-M047 … CR-M055 (8 closed; M051/IR deferred)

- **gRPC M047** — `WithAuth` copies inbound headers into a new `Metadata` instead of mutating the
  caller's, so reused `CallOptions` no longer accumulate duplicate auth headers.
- **Hardware Serial M048/M049** — ctor guard-throws on null settings (was a latent NRE); all
  `ReadData` access serialized on a dedicated `_readLock` (atomic RemoveReadData also fixes a `-1`
  crash). New **Hardware.Tests** (M050, 23) covers GetID + buffer logic.
- **Modbus M052** — MBAP transaction id validated against the response; mismatch throws instead of
  accepting a stale frame. Regression test added.
- **Network M054** — `Udp.Write` guards `_remoteEndPoint` (CS8604 + latent NRE). New **Network.Tests**
  (M055, 25). M053 (RemoveReadData TOCTOU) was already atomic (CR-H026/H027) — not reproducible.
- Suites green: gRPC 25, Modbus 71, Hardware 23, Network 25.
- **Deferred:** CR-M051 (IR test gaps) — next batch with NFC/OAuth test-gaps.

### Batch 5 — Communication.Camera + .GraphQL CR-M043 … CR-M046 (4 findings, all closed)

- **Camera M043** — `CaptureFrameAsync` kills the ffmpeg process tree in a `finally` on cancel/fail
  (Process.Dispose doesn't terminate the child), so no orphaned ffmpeg holds the camera.
- **Camera M044** — arguments now go through `ProcessStartInfo.ArgumentList` (extracted `BuildArguments`),
  unquoted defaults; tests assert a space-containing device is one token and injection is impossible.
- **GraphQL M045** — removed the `_requestLock` SemaphoreSlim that serialized every request on a shared
  client; regression test proves two concurrent requests are in flight at once.
- **GraphQL M046** — extracted `HandleMessageAsync` from the receive loop and unit-tested the
  next/complete/error/wrong-id/payload-errors dispatch (frame reassembly was already covered).
- Suites green: Camera 15, GraphQL 58.

### Batch 4 — Communication.Bluetooth cluster CR-M036 … CR-M042 (7 findings, all closed)

- **M036** — `Read()` now checks availability + `GetRange` under one `lock(ReadData)` (was a TOCTOU);
  platform-agnostic, tested.
- **M037** — Linux discovery wraps the timeout CTS, linked CTS, and bluetoothctl `Process` in `using`
  (compile-verified with `DefineConstants=LINUX`).
- **M038** — a `_reconnecting` guard stops `Open()` resetting `_reconnectAttempts` mid-reconnect, so
  `MaxReconnectAttempts` now actually bounds the retry chain (was effectively infinite). Residual
  thread-supervision documented inline.
- **M039** — `OpenWindows` checks `connectTask.Wait(timeout)` and throws on timeout (WINDOWS-gated;
  code-review verified — WinRT branch isn't compiled off a Windows TFM).
- **M040** — CLAUDE.md rewritten to the real API; README examples corrected (the fictional
  `BLEServer`/`BLEScanner`/etc. types are gone).
- **M041** — dead RSSI branch removed; Linux service-filtered discovery throws `NotSupportedException`
  instead of silently returning all devices (Windows placeholders left documented).
- **M042** — `Birko.Communication.Bluetooth.Tests` already existed; augmented with `Read()` + `GetID`
  tests (12, hardware-free).
- **Note:** the Windows WinRT branches (M038/M039 Windows parts) are code-review-only — they don't
  compile off a Windows TFM here; the Linux branch was compile-checked (a pre-existing `sizeof`/`unsafe`
  issue there is unrelated to these findings and left untouched).

### Batch 3 — Caching cluster CR-M030 … CR-M035 (6 findings, all closed)

- **MemoryCache** (CR-M030): per-key stampede locks are now reference-counted and retired by their
  last releaser — the timer no longer evicts locks, closing the GetOrAdd↔WaitAsync eviction race.
- **HybridCache** (CR-M031): write-through `SetAsync` always awaits the L1 write in a `finally`, so
  the L1 write is never an orphaned fire-and-forget when L2 faults with fallback disabled. Tests
  expanded (CR-M032): L1 TTL capping, WriteThrough=false, L2-failure fallback for
  Remove/Exists/RemoveByPrefix/Clear.
- **RedisCache** (CR-M034): every method observes the `CancellationToken` (+ the RemoveByPrefix loop);
  offline regression tests exploit the lazy connection. CR-M033 (dead `KeyTimeToLiveAsync`) was already
  gone (fixed with CR-H014); CR-M035's test project already existed — expanded with the cancellation tests.
- Caching suites green: Caching 25, Hybrid 34, Redis 12.

### Batch 2 — BackgroundJobs cluster CR-M015 … CR-M029 (15 findings, all closed)

Across all 8 job-queue backends. Bugs fixed + regression-tested; audit `Status` flipped to `done`.
- **Atomic dequeue claim** ported from the SQL backend's proven `ClaimToken` conditional-update +
  re-read-verify pattern to the doc stores: **MongoDB** (CR-M020, per-document `$set` atomic),
  **RavenDB** (CR-M021, single-winner even under last-write-wins), **ElasticSearch** (CR-M016,
  refresh-window residual documented — handlers must be idempotent). `ClaimToken` added to each model.
- **File backends** JSON (CR-M018) / XML (CR-M029): `DequeueAsync` read-claim-update serialized with
  a `SemaphoreSlim` (mirrors `InMemoryJobQueue`); cross-process still unsupported by design (documented).
- **Redis:** `GetByStatusAsync` orders before truncating (CR-M023); every method + `RedisJobLockProvider`
  observe the `CancellationToken` (CR-M024); the dequeue claim (ZREM + hash flip) is now fully inside
  the Lua script, closing the orphan window (CR-M025).
- **SQL lock provider:** connection no longer leaks on `OpenAsync` failure (CR-M026); non-Postgres
  backends get real locks (MSSql `sp_getapplock`, MySQL `GET_LOCK`) or return `false` instead of a
  false success (CR-M027, SQLite).
- **Test projects:** created `Birko.BackgroundJobs.{JSON,ElasticSearch,RavenDB,MongoDB}.Tests`
  (CR-M017/M019/M022 + M020 coverage); CosmosDB/SQL already existed (CR-M015/M028) — SQL expanded with
  lock-provider + FailAsync-boundary tests. All 8 BackgroundJobs suites green (JSON 6, XML 7, Redis 7,
  SQL 17, CosmosDB 3, Mongo 6, ES 6, Raven 6).

### Batch 1 — Birko.AI cluster CR-M001 … CR-M014 (14 findings, all closed)

Across Birko.AI, .Agents, .Contracts, .Orchestration, .Providers, .Resilience.

- **Bugs fixed + regression-tested:** CR-M001 (async `HandleResponse`), CR-M002/CR-M008
  (`CancellationToken` threaded through `ILlmProvider` + all 16 providers + `LlmProviderBase` retry
  loops/SSE + `TrackedLlmProvider` + the agent run loop + `Tool.ExecuteAsync`), CR-M003
  (`LlmStreamingResponse` made disposable, disposed via `await using`), CR-M007 (symmetric
  `ToDictionary`/`FromDictionary`), CR-M010 (`SendWithRetryAsync` disposes the buffered response),
  CR-M011 (rate-limit re-check loop), CR-M012 (`GetRetryAfter` covers token/daily windows), CR-M013
  (serialized circuit-breaker persistence + `FlushPersistenceAsync`).
- **Not reproducible:** CR-M006 (`Merge` already copies `OnLlmResponseReceived`).
- **Test-gap:** CR-M005 → new `Birko.AI.Agents.Tests` (18 tests). CR-M004 / CR-M009 / CR-M014 →
  their `.Tests` projects already existed (created since the audit) and were expanded with regression
  tests for the fixes above.
- New AI-cluster test count: **59** (AI.Tests 11, Agents.Tests 18, Providers.Tests 17,
  Orchestration.Tests 3, Resilience.Tests 10). Remaining medium findings CR-M076 … CR-M275 are still
  to triage (verify-first, one project cluster at a time).

## User story

As a maintainer, I want the **medium**-severity code-review findings triaged and the worthwhile
ones fixed, so quality issues below the high bar don't accumulate.

## Scope

The 275 medium findings `CR-M001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
these are reviewer claims that were not individually adversarially re-checked, so confirm each is
real before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Mxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
