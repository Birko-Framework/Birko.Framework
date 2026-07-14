---
id: STORY-027
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: low
finding-count: 418
finding-ids: CR-L001 …
---

# Low findings

## Progress

**134 / 418 triaged** as of 2026-07-14. Next open is CR-L135 (Birko.Data.JSON.ViewModel or next project).

**Batch K — Data.JSON cluster (CR-L129 … CR-L134):** JSON + JSON.ViewModel.
**Bugs fixed:** L129 (async bulk `UpdateCoreAsync` guards `ContainsKey` before writing + only saves when
something changed — was silently upserting a non-existent item, unlike single-item + sync-bulk Update),
L130 (`AsyncJsonBatchStore.SetSettings(Settings)` uses `is not BatchSettings` → clear `InvalidDataException`
instead of an opaque `InvalidCastException` from the old `(BatchSettings)` hard-cast, matching the sync
batch stores), L131 (`JsonStore.LoadData` + `JsonBatchStore` + `JsonBatchBulkStore` loaders guard
`item?.Guid.HasValue == true` before `_items.Add` — a record with a missing/null guid used to NRE via the
`Guid!.Value`; matches the async/separate loaders). **Convention:** L132 (`AsyncJsonStore.GetPath` guards
both `Location` and `Name`, aligning with the sync `JsonStore.GetPath` — behavior already converged via
`GetDirectory()`). **Cleanup:** L134 (removed the unused `using Birko.Configuration;` from both
JSON.ViewModel repo files — `using Birko.Data.Stores;` kept, needed for the extension methods). **New
project:** L133 (**Birko.Data.JSON.ViewModel.Tests** — sync/async repo ctor guard + JsonStore unwrap, 5
tests; git-init'd + registered in `.slnx`/`.code-workspace`). **Tests:** JSON.Tests +4 (async-bulk no-upsert,
batch SetSettings clear-throw + accept, LoadData null-guid skip via a hand-written camelCase file located
through the public `GetPath()`). **/code-review: clean (no findings).** Suites green: JSON 18,
JSON.ViewModel 5.

**Batch J — EventSourcing + InfluxDB + InfluxDB.ViewModel + InMemory (CR-L120 … CR-L128):**
**Fixes:** L122 (InfluxDB `MapRecordToModel` outer catch now rethrows `InvalidOperationException` with
the target type instead of silently `return default` — the per-property inner catch still skips a single
bad field, but a structural/constructor failure no longer vanishes via the bulk read's `Where(m != null)`
and mask round-trip bugs; sync + async), L123 (added a public `AsyncInfluxDBStore.Settings` accessor;
`InfluxDbUnitOfWork.FromStore` reads Bucket/Organization through it instead of **reflecting** the private
`_settings` field). **Cleanup:** L120 (removed the unused `using Birko.Configuration;` from all 5
EventSourcing `Stores/` files — `System.Linq.Expressions` is actually *used* by the 4 wrappers, so kept;
the finding's claim it was unused in the Extensions file was stale — that file never imported it), L125
(removed the unused `using Birko.Configuration;` from both InfluxDB.ViewModel repo files — the finding also
named `using Birko.Data.Stores;` but that's **required** for the `GetUnwrappedStore`/`IsStoreOfType`
extension methods, so kept). **Docs/verify-first:** L121 (documented InfluxDB `Settings` intentionally
extends `Configuration.Settings` directly, not RemoteSettings — token+org auth, no user/password/port),
L124 (documented the accepted bulk filter-override gap — InfluxDB has no native update-by-predicate;
native filter-Delete is feasible but live-only, deferred; base fallback correct), L126 (the double-destroy
this finding worried about is already fixed in both repos under CR-M096; shared-base extraction deferred —
parallel sync/async hierarchies), L127 (documented InMemory `SetSettings(ISettings)` cast-or-no-op is
intentional — the store never reads settings). **wontfix:** L128 (InMemory `_settings`/`SetSettings`
duplication — the finding itself says leave as-is; no shared base across the sync/async abstract stores,
~20 lines). **Tests:** InfluxDB.Tests +1 (`Settings` accessor + `FromStore`-without-reflection). L122 is a
live-cluster read path (code-review verified — same sanctioned surfacing as ES L114). **/code-review: 1
PLAUSIBLE** (L122 read-now-throws on a structurally-corrupt row — documented, kept). Suites green:
EventSourcing 5, InfluxDB 26, InfluxDB.ViewModel 5, InMemory 40.

**Batch I — Data.ElasticSearch cluster (CR-L111 … CR-L119):** ElasticSearch (store), .ViewModel, .Views.
**Fixes:** L111 (filter-based `Delete`/`Update` overrides in both stores now call `EnsureInitialized`/
`EnsureInitializedAsync` first — lazy-init + cancelled-token gate, matching every base CRUD method),
L112 (`StoreAggregationHelper` composite-bucket `TryGetValue(out string keyValue)` → `out string?` ×2,
clears the CS8600 risk), L113 (extracted a shared `ElasticSearchStoreHelper` — `ResolveIndexName`/
`SanitizeIndexName` + `BuildUpdateScript<T>` — so the sync/async stores stop copy-pasting the index-name
sanitization and the PropertyUpdate→Painless script builder), L114 (bulk per-item failures in an
otherwise-valid response now **throw** with the offending items instead of a swallowed `// TODO` — matches
the single-item paths + UnitOfWork; ES bulk is non-atomic so the successful items are already persisted,
documented inline + flagged by /code-review as PLAUSIBLE, intended), L118 (`ExecuteSimpleQueryAsync`
clamps Size via a new internal `ClampWindowSize` so `From + Size` stays within the ES default
`max_result_window` of 10000 — a non-zero offset with the default size used to exceed it and get rejected),
L119 (`SetPropertyValue` handles enum/Guid targets via a new internal `ConvertValue` — `Convert.ChangeType`
silently dropped every enum/Guid group-by/aggregate column). **API asymmetry:** L115 (async repo gains
`CountAsync(QueryContainer)`/`ClearCacheAsync`/`ReadAsync(SearchRequest)` delegating through the unwrapping
`ElasticSearchStore` property, mirroring the sync repo). **Cleanup:** L117 (removed unused
`using Birko.Configuration;` from both repo files). **Verify-first:** L116 (Birko.Data.ElasticSearch.
ViewModel.Tests already exists — M092 — augmented with async-repo ctor guard + unwrap + CountAsync-zero).
**Tests:** Views.Tests +12 (`ConvertValue` enum/Guid/int/fail matrix + `ClampWindowSize` boundaries),
ViewModel.Tests +3 (async repo). L111/L114 are live-cluster paths (code-review verified). **/code-review:
1 PLAUSIBLE (the sanctioned L114 partial-commit-then-throw semantics — documented, kept).** Suites green:
ES.Tests 78, ViewModel.Tests 6, Views.Tests 19.

**Batch H — Data.CosmosDB (CR-L103 … CR-L110):** **Fixes:** L103 (`IsHealthyAsync` observes the ct),
L106 (removed `AsyncCosmosDBRepository.DestroyAsync` double-destroy override — base is sufficient), L109
(`CosmosViewManager.DropAsync` idempotent on NotFound), L110 (`CosmosViewStore` takes the SQL path for
group-by-only views, not just aggregate views — was returning ungrouped docs via LINQ). **New project:**
L108 (**Birko.Data.CosmosDB.ViewModel.Tests** — repo ctor guard + unwrap, 5 tests). **Docs/verify:** L104
(bulk filter override = accepted base fallback), L105 (CRUD/UnitOfWork need the emulator — deferred), L107
(SetSettings(RemoteSettings) already accepts Cosmos Settings via upcast). **/code-review: clean** (verified
the group-by SQL builder handles zero-aggregate views). Suites green: CosmosDB 43, CosmosDB.Views 7,
CosmosDB.ViewModel 5.

**Batch G — core foundation (CR-L094 … CR-L102):** Configuration, Contracts, CQRS, Data.Aggregates,
Data.Core. **Fixes:** L094 (`PasswordSettings.Password` defaults to `string.Empty` not `null!` — no
consumer distinguishes null from empty), L095 (`Contracts.RetryPolicy.GetDelay` clamps `attemptNumber` to
≥1), L098 (`CQRS/Unit.cs` adds `using System.Threading.Tasks;` for self-containment). **Docs/verify:**
L096 (Contracts CLAUDE.md `IDefault.Default`→`IsDefault`), L097 (Mediator static-cache process-wide intent),
L100 (AggregateMapper.Expand emits insert/delete only), L101 (ICopyable nullability contract — full
alignment deferred, would cascade warnings across ~15 implementers), L102 (LogViewModel duplication
deferred — parallel hierarchies). **Tests:** CQRS pipeline exception-propagation + pre-cancelled-token,
Contracts clamp Theory, Configuration empty-password. **/code-review: clean (no findings).** Suites green:
Configuration 12, Contracts 13, CQRS 30.

**Sub-batch F2 (CR-L084 … CR-L093) — SOAP + SSE + WebSocket:** **L084** (SOAP Send* helpers close the
OutputStream in `finally`), **L085** (StreamReader honors `request.ContentEncoding`), **L086** (extracted a
shared `SoapXml` Escape/BuildFault helper — 3 duplicated copies → 1, byte-identical output), **L087**
(removed dead query-strip in GetServicePath), **L088** (token query-string splits on first `=`), **L089**
(`SseEvent.ToString` emits explicit LF, not AppendLine's CRLF), **L090** (deferred — SendLoop Channel
refactor is untestable-live cleanup; correctness fine), **L091** (removed unused usings in SseServer),
**L092** (verify-first — WebSocketPort.Write no-op catch already gone, CR-M073), **L093** (WebSocketServer
`BroadcastAsync` is best-effort — one failed client no longer aborts the whole broadcast). Suites green:
SOAP 7, SSE 19, WebSocket 33.

**Communication cluster summary (A–F2, CR-L041 … CR-L093, 53 findings):** 2 new test projects created
(Birko.Communication.Tests, Birko.Communication.OAuth.Providers.Tests), `IPort : IDisposable` added,
numerous bug fixes (query-string `=` handling ×3, OutputStream `finally` close ×2, NDEF big-endian, OAuth
poll order + refresh retry, Modbus early-exit, Network thread-join, best-effort broadcast), and a batch of
doc/verify-first closures.

**Sub-batch F1 (CR-L078 … CR-L083) — REST + REST.Server:** **L078** (verify-first — `_clients` already a
ConcurrentDictionary with GetOrAdd), **L079** (documented OnRequest/OnResponse handlers must not throw —
they run inline), **L080** (added static-cache tests; SendRequestAsync HTTP-path seam noted as residual),
**L081** (RestServer route match iterates once + reuses the parameters dict instead of running IsRouteMatch
twice), **L082** (query-string parser splits on the first `=` so token/JWT values containing `=` aren't
dropped), **L083** (response `OutputStream.Close()` moved into a `finally` in both SendResponseAsync and
SendServerErrorAsync so a mid-write client disconnect doesn't leave the connection half-open). Suites green:
REST 22, REST.Server 7.

**Sub-batch E (CR-L070 … CR-L077) — NFC + OAuth + OAuth.Providers:** **L070** (`SerialNfcTransport.TransceiveAsync`
wraps the blocking serial Write/Read in `Task.Run` + observes the token, mirroring ReadTagAsync), **L071**
(documented `NfcReaderPort.Write` blocks + drops the APDU response — use `TransceiveApduAsync`), **L072**
(documented `NfcReaderSettings` intentionally stays on `PortSettings`), **L073** (`NdefRecord.GetText` UTF-16
now defaults to big-endian per the NFC Forum spec, honoring a LE BOM — was UTF-16LE unconditionally),
**L074** (OAuth `GetTokenAsync` no longer re-issues an identical just-failed refresh under the RefreshToken
grant — rethrows + removed the dead switch arm), **L075** (device-code poll issues the first request
immediately, delaying only after authorization_pending/slow_down per RFC 8628), **L076** (GitHub
`CreateDeviceFlowClient` delegates to `CreateDeviceFlowSettings` — single source of truth), **L077** (new
**Birko.Communication.OAuth.Providers.Tests** project, git-init'd + registered). Suites green: NFC 121,
OAuth 54, OAuth.Providers 5.

**Sub-batch D2 (CR-L065 … CR-L069) — Modbus + Network:** **L065** (dropped the always-overwritten dead
`expectedMinResponse` parameter of `SendWriteRequest` + the four `8` call-site args), **L066** (documented
Modbus client as synchronous-by-design, no `CancellationToken` — inherent to the sync IPort contract),
**L067** (response-wait loop exits early on a complete error frame via `IsCompleteErrorResponse()` instead
of spinning the full timeout), **L068** (verify-first — exception-response + TCP tx-id mismatch already
covered by CR-H025/CR-M052; chunked-buffer MockPort residual noted), **L069** (TcpIp/Udp `ReadWorker`
capture local `_stream`/`_client` refs + `Close()` joins the read thread with a 500ms timeout — Udp closes
the client first to unblock its blocking `Receive`). Also added `MockPort.Dispose()` in Modbus.Tests
(ripple from the L043 `IPort : IDisposable` change). Suites green: Modbus 71, Network 25.

Communication cluster remaining: NFC (L070–L073), OAuth + OAuth.Providers (L074–L077), and the web
protocols REST/SOAP/SSE/WebSocket (L078 onward).

**Sub-batch D1 (CR-L059 … CR-L064) — Hardware + IR:** **L059** (verify-first — Serial Read/HasReadData/
RemoveReadData already guard size<0, "negative = all", CR-M049), **L060** (removed a no-op `catch(Exception){throw;}`
in `Serial.Open`), **L061** (rewrote Hardware CLAUDE.md to the real Serial/Infraport/LPT surface; README was
already accurate), **L062** (`InfraredPort.HandleReceivedTiming` tries RawProtocol last regardless of
registration order — RawProtocol matches any non-empty timing; + regression test), **L063** (documented
`SamsungAcProfile.Protocol` as informational — transmit AC via `GetTiming()`), **L064** (documented
`IrTiming.TotalDurationUs` as single-pass, excluding repeats). Suites green: Hardware 23, IR 103.

**Sub-batch C (CR-L053 … CR-L058) — gRPC + gRPC.Server:** **L053** (Credentials doc corrected to
attribute scheme-based inference to `GrpcChannel.ForAddress`, not the pool), **L054/L055** (`DeadlineSeconds`
+ `ExtraMetadata` documented as reserved/not-auto-applied — kept, removing is breaking), **L056**
(verify-first: the client interceptor overrides only unary calls — no streaming overrides exist to test;
the settings-based CreateClient path is integration-tier), **L057** (added the three streaming server-handler
auth-gate tests — Client/Server/Duplex — gRPC.Server.Tests 5→11), **L058** (verify-first: `EnableReflection`
is a host-honored intent signal, already round-tripped by GrpcServerSettingsTests). Suites green: gRPC 25,
gRPC.Server 11.

**Batch 4 — Communication cluster (in progress).** Worked in sub-batches by project.
- **Sub-batch B (CR-L048 … CR-L052) — Camera + GraphQL:** **L048** (`FfmpegCameraSettings.JpegQuality`
  clamped to [1,31] in the setter, so an out-of-range value can't silently fail the ffmpeg capture),
  **L049** (verify-first — settings/frame tests exist; happy path needs ffmpeg; added a JpegQuality clamp
  Theory), **L050** (GraphQL subscription frames — connection_init/pong/subscribe — now use the shared
  `_serializer` so variables are camelCased consistently with the HTTP path, instead of raw default-options
  `System.Text.Json`), **L051** (`SchemaPath`/`SubscriptionProtocol`/`EnableAutoPersistedQueries` documented
  as reserved/not-yet-implemented — kept, since removing them is breaking), **L052** (dropped an unused
  `System.Text.Json` using). Suites green: Camera 21, GraphQL 58.
- **Sub-batch A (CR-L041 … CR-L047) — core + Bluetooth:** **L041** (`PortSettings.GetID` typo
  `AbstratPort`→`AbstractPort`), **L042** (`AbstractPort.InvokeProcessData` public→protected — fired
  internally by derived ports, not part of the IPort contract), **L043** (`IPort : IDisposable` +
  `AbstractPort.Dispose()`→`Close()` with a `_disposed` guard; the 3 ports that already had `Dispose`
  — Serial/NFC/BluetoothLE — became `override`), **L044** (new **Birko.Communication.Tests** project,
  git-initialized + registered in `.slnx`/`.code-workspace`, 6 hardware-free tests via an in-memory
  `AbstractPort` subclass). Bluetooth (platform-gated): **L046** (read worker surfaces faults via a new
  `ReadError` event instead of a bare `catch{break;}`), **L045** (WinRT discovery keys by `args.Id`
  instead of the not-always-present address property — code-review-only), **L047** (Linux P/Invoke uses
  `Marshal.SizeOf<SockaddrL2>()` + `[StructLayout(Sequential)]` — compile-checked with `DefineConstants=LINUX`).
  Suites green: Communication.Tests 6; Bluetooth/Hardware/NFC test projects build clean.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.
**Bugs fixed:** L036 (`MemoryCache.GetAsync` degrades a type-mismatch to a Miss via `is T` instead of the
unchecked `(T)Value!` cast that threw `InvalidCastException`; a stored null is still a hit), L040
(`RedisCache.RemoveByPrefixAsync` batches deletes via the `KeyDeleteAsync(RedisKey[])` array overload
instead of one round-trip per key — ct was already checked under CR-M034). **Convention:** L034 (the six
`MemoryCache` async CRUD methods now observe the `CancellationToken`). **Hardening:** L035 (largely resolved
by CR-M030 — `EvictExpired` no longer sweeps `_locks`; added a `volatile _disposed` flag + guard as belt-and-
suspenders). **Docs:** L038 (documented the intentional L2-hit L1-population staleness cap that differs from
GetOrSet). **Test-gaps:** L037 (added sliding-expiration + CacheSerializer round-trip tests; stampede + L1Max
already covered; NeverRemove-in-EvictExpired left uncovered — not observable without reflection), L039
(`GetL1Options` made `internal` + a full matrix test: null / absolute-below/above-max / sliding-only / null-max).
Suites green: Caching 40, Hybrid 39, Redis 12.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.
Verify-first. **Bugs fixed:** L014 (`RetryPolicy.GetDelay` overflow → compute in double, saturate at
MaxDelay), L019 (`JobExecutor` typed path returned `JobResult.Failed` when the matched `ExecuteAsync`
yields a null Task instead of masquerading as Succeeded), L025/L029 (JSON + RavenDB `FailAsync` fall back
to `RetryPolicy.MaxRetries` when the job's own MaxRetries is 0, mirroring `InMemoryJobQueue`), L021 (Cosmos
dequeue `ScheduledAt != null` guard), L020 (Cosmos FIFO tiebreaker `ThenBy(EnqueuedAt)`). **Convention/
cleanup:** L017 (`InMemoryJobQueue.EnqueueAsync` stamps `EnqueuedAt` from the injected clock), L022 (Cosmos
`FailAsync` signature matches the interface's non-nullable error), L023 (removed dead ES `IndexName` const),
L027 (removed dead Mongo `CollectionName` prop), L031 (Redis lock-release Lua extracted to one const +
shared sync/async helpers), L015 (documented `JobStatus.Failed` as reserved/unused), L024/L030 (corrected
stale "called automatically" schema doc-comments), L026/L033 (documented the intentional JSON-metadata
serializer + XML `Delay`-resolved-by-pipeline behavior). **Comment-only + deferred:** L016 (fixed the
misleading "re-enqueue" comment; a true no-retry requeue needs a dedicated `IJobQueue.RequeueAsync` — every
backend `EnqueueAsync` is an insert, so re-enqueuing the same id would PK-conflict). **Verify-first (already
resolved / not-a-defect):** L018 (helper is used by backend models — finding scoped to a core-only checkout),
L028 (`Birko.BackgroundJobs.MongoDB.Tests` already exists, CR-M022), L032 (SqlJobLockProvider already
dispatches per dialect, CR-M027). Suites green: core 77, JSON 7, RavenDB 7, Cosmos 3, ES 6, Mongo 6,
Redis 7, XML 7.

**Batch 1 (2026-07-14) — Birko.AI cluster (CR-L001 … CR-L013):**
`Birko.AI` / `.Agents` / `.Contracts` / `.Providers` / `.Resilience`. Verify-first (unverified reviewer
claims). Closed: **L001** (snapshot conversation before the sync fallback so a partially-mutated
streaming turn isn't resubmitted), **L002** (dead `Done ? conversation : conversation` ternary +
`HandleResponse`/`HandleToolUse` return type simplified from the unused `(Done,Continue)?` tuple to
`bool?`), **L003 partial** (removed the redundant all-errors reflection in Agent.cs via an `errorCount`;
the cross-provider `ToolResult` record was deliberately not introduced — the anonymous shape is JSON-
serialized to the wire with exact snake_case keys, so a record risks breaking every provider),
**L004/L009** (double-checked-lock `RegisterAll` in `AgentRegistration`/`ProviderRegistration`),
**L005** (`AgentOptions.FromDictionary` uses `TryParse` so a malformed config value is skipped, not a
`FormatException`), **L006** (`LlmProviderFactory` → `ConcurrentDictionary`), **L007** (new
`LlmProviderFactoryTests` + `FromDictionary` tolerance tests in the existing `Birko.AI.Tests`),
**L008 verify-first** (already resolved by CR-M003 — `LlmStreamingResponse` is `IDisposable`/
`IAsyncDisposable`), **L010** (stale default Claude model `claude-3-5-sonnet-latest` → `claude-sonnet-4-6`),
**L011** (Claude/Gemini tool-arg parsing deserializes the whole input object into `Dictionary<string,object>`
so nested JSON structure survives round-tripping, instead of per-property `ToString()`), **L012**
(`CheckBudgetAsync` short-circuits when `_config.Enabled` is false), **L013** (`ProviderRateLimiter`
last-wins dictionary build, no throw on case-duplicate providers). Suites green: `Birko.AI.Tests` 20,
`.Resilience.Tests` 13, `.Providers.Tests` 17, `.Agents.Tests` 18. Statuses flipped in the audit.

## User story

As a maintainer, I want the **low**-severity code-review findings triaged so genuine nits get
cleaned up opportunistically without derailing higher-priority work.

## Scope

The 418 low findings `CR-L001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
reviewer claims, not adversarially re-checked; many are stylistic. Confirm value before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Lxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
