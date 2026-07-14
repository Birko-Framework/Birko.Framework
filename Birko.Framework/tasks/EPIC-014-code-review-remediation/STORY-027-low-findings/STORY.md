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

**52 / 418 triaged** as of 2026-07-14 (Communication sub-batches A–B done).

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
