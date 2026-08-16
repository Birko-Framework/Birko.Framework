---
id: TASK-223
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-220, TASK-222]
pr: [Birko.Data.CosmosDB@2ad57be, Birko.Data.CosmosDB.Tests@326a4ff]
github-issue: null
jira-key: null
findings: []
---

# CosmosDB's connection mode cannot be selected — Gateway is unreachable, so the emulator is too

## Context

Found 2026-08-16 while answering *"can Cosmos run from Docker?"* — it can, and the framework still
cannot talk to it.

`Settings.GetCosmosClientOptions()` sets `RequestTimeout`, `AllowBulkExecution` and the serializer, but
**not `ConnectionMode`**, so the SDK default (**Direct**, over TCP) always applies. `AsyncCosmosDBStore`'s
connection-string constructor — the one the live suite uses — offers no way in either, and a Cosmos
connection string cannot carry a connection mode.

Measured against `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` (HTTP):

| via | result |
|---|---|
| the store's connection-string ctor (Direct, the only option) | `InvalidOperationException: The stream was already consumed`, thrown from `GatewayAddressCache.GetMasterAddressesViaGatewayAsync` → `AddressSelector.ResolveAllTransportAddressUriAsync` — Direct-mode physical-address resolution |
| a `CosmosClient` with `ConnectionMode.Gateway`, injected through the `AsyncCosmosDBStore(Container)` ctor | works: container created, 4 documents seeded and read back, and `bareBool` / `inArray` / `ternary` / `coalesceCmp` / `arithAdd` / `arithMul` all match the compiled-delegate oracle |

**Two consequences, and the second is the one that matters.**

1. `CosmosFilterMatrixLiveTests` cannot run. It is gated on `BIRKO_COSMOS_CONNECTION`, and even with the
   variable set against a working emulator it fails at address resolution. That makes it the **last dark
   suite in this family** — [[TASK-214]], [[TASK-218]], [[TASK-221]] and [[TASK-222]] were every one of
   them found by running a suite that had never run.
2. **A consumer behind a corporate proxy or a restrictive firewall cannot use the Cosmos store at all.**
   Gateway mode exists precisely for that case and Microsoft documents it as the fallback when the
   Direct-mode TCP port range is blocked. The framework offers no way to ask for it.

The emulator is a symptom; (2) is the defect.

## Audit — does any other provider have a Gateway equivalent?

Asked directly at pick time. **No — Gateway is Cosmos-specific**, because Cosmos is the only provider
here with *two transports*:

| provider | transport | Gateway analogue? |
|---|---|---|
| **CosmosDB** | Direct (TCP to replicas) **or** Gateway (HTTPS to the endpoint) | **this task** |
| MongoDB, Redis | wire protocol over TCP only | none — no HTTP mode exists |
| RavenDB, ElasticSearch, InfluxDB | HTTP(S) only | none needed — already traverses a proxy |

So there is nothing to add elsewhere under this name. **But the underlying need — "work through a
restricted network" — does generalise, and the audit found the same shape of gap in MongoDB:**
`Settings.GetConnectionString()` composes the URI and appends a *fixed* set of query parameters, with no
`RawConnectionString` override. A consumer cannot set `maxPoolSize`, `appName`, `connectTimeoutMS`,
`directConnection`, or MongoDB's SOCKS `proxyHost`/`proxyPort` — the closest thing it has to Gateway.
`Birko.Redis.RedisSettings` already solves exactly this with `RawConnectionString`; MongoDB, InfluxDB and
TimescaleDB do not. Filed as [[TASK-225]] — and it is not hypothetical: every live-MongoDB probe in
[[TASK-214]] and [[TASK-219]] had to subclass `Settings` to set a timeout.

## Approach

1. Add `ConnectionMode` to `Birko.Data.CosmosDB.Stores.Settings` (default `Direct`, matching today's
   behaviour so nothing changes for existing consumers) and honour it in `GetCosmosClientOptions()`.
2. Decide how the connection-string constructor reaches it — probably an optional `Settings` /
   `CosmosClientOptions` parameter rather than widening the string.
3. Make the gated suite *runnable* against the emulator and record its first full shape report. Expect
   the four shapes above plus the array `IN` from [[TASK-220]].
4. Consider whether `PreferredRegions` / `LimitToEndpoint` belong in the same pass — the other two knobs
   a real deployment needs, absent for the same reason. **Decide explicitly; do not drift into it.**

## Acceptance criteria

- [x] A consumer can select Gateway mode through `Settings`, and the default is unchanged (`Direct`) —
      pinned by `The_default_is_unchanged`, and `LoadFrom` carries it too
- [x] `CosmosFilterMatrixLiveTests` runs against the Docker emulator and its **full shape report** is
      recorded in this task — the report is the point, not the pass. **26 of 27**, see § Result
- [x] Any divergence it reveals is fixed or filed; none left unexplained — one, `dateDotDate`, filed as
      [[TASK-224]]. **Filed as a defect, not accepted into a ledger**: it is a silent wrong answer
- [x] A non-gated test pins that `GetCosmosClientOptions()` honours the setting — `CosmosConnectionModeTests`, 5 tests
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6

## Out of scope

- [[TASK-222]]'s normalizer work. Cosmos was measured during this investigation to honour ternary,
  coalesce and arithmetic, and that finding is already recorded in `ExpressionNormalizer`'s comment.

## Human test plan

- [x] `docker run --rm -p 8081:8081 mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview --protocol http`,
      wait for *"System is now fully ready to accept requests"*, then run the suite with
      `BIRKO_COSMOS_CONNECTION` set and read the whole report. **Done** — and reading the whole report is
      what found `dateDotDate`; the pass/fail line alone would have said only "1 diverged".

## Result — the suite's first run ever: 26 of 27

```
bareBool  constTrue  negation  rangeAmount  inClosure  enumEq  guidEq  decimalCmp   -> OK
startsWith  endsWith  contains  toLowerEq  dateRange                                -> OK
dateDotDate                                                    -> DIVERGE oracle=1 actual=0
nestedAny  nestedMember  eqNull  notEqNull                                          -> OK
grpOrAnd  grpAndOr  deMorgan  deepNest  mixedNot                                    -> OK
ternary  coalesceCmp  arithAdd  arithMul                                            -> OK
```

Three things this settles beyond the connection mode:

- **`inClosure` OK** confirms [[TASK-220]]'s array-`Contains` rewrite end-to-end against a real Cosmos
  service. Until now it had only been render-tested.
- **`ternary` / `coalesceCmp` / `arithAdd` / `arithMul` OK** closes the caveat [[TASK-222]] left in
  `ExpressionNormalizer`'s comment, which recorded CosmosDB as *unverified*. It is verified now, and the
  comment says so.
- **`dateDotDate` is a silent wrong answer** — [[TASK-224]].

## Outcome

**What was broken.** `GetCosmosClientOptions()` never set `ConnectionMode`, so the SDK default (Direct,
over TCP) always applied and nothing could ask for anything else — a Cosmos connection string cannot
carry a connection mode, and the store's connection-string constructor took no options. Gateway is
Microsoft's documented fallback where the Direct port range is blocked, so **a consumer behind a
corporate proxy or firewall could not use the Cosmos store at all**. The unreachable emulator was the
symptom that exposed it.

**The fix.** `ConnectionMode` on `Settings`, defaulting to `Direct` so nothing changes for anyone;
honoured in `GetCosmosClientOptions()`, carried by `LoadFrom`, and reachable through an optional
`Settings` parameter on both connection-string constructors.

**Judgement calls.**

- **Default stays `Direct`.** Gateway is slower; making it the default to suit an emulator would have
  changed the transport of every existing deployment for a test's convenience.
- **`LoadFrom` carries it.** The three sibling settings are copied there, and omitting the fourth loses
  the mode silently wherever settings are cloned or loaded from configuration — a wrong transport with
  no error, which is this session's recurring failure mode in miniature.
- **An optional constructor parameter, not a widened connection string.** The string is an Azure format;
  inventing an extension to it would be a private dialect no other tool understands.
- **`PreferredRegions` / `LimitToEndpoint` deliberately NOT added.** The task listed them as candidates.
  Neither was needed to reach the emulator, and adding configuration nobody has asked for or measured is
  how a settings class becomes a junk drawer. Recorded as considered and declined.
- **The matrix suite is left FAILING on `dateDotDate`.** Adding it to an accepted-divergence ledger — as
  [[TASK-222]] did for RavenDB's five *loud* refusals — would be normalising a silent wrong answer. Fix
  what lies; document what refuses.

**Flagged, not fixed.**

- **[[TASK-224]]** — `.Date` renders as `root["CreatedAt"]["Date"]`, a sub-property of a string, so it
  matches nothing and reports no error. Diagnosed to the emitted SQL, so it is close to turnkey.
- **[[TASK-225]]** — MongoDB has no connection-string escape hatch, the same shape of gap this task fixes
  for Cosmos. Redis already solved it with `RawConnectionString`.

## Implementation plan

_Populated by `/tasks plan TASK-223` — leave empty until then._

## Progress log

- step 2 — picked; user asked for it directly, together with "do all other providers have gateway?" — so the audit of the other remote providers rides along and is recorded below.
- step 3 — verified. The emulator (mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview, --protocol http) is reachable and serves the framework normally in Gateway mode; through the store's own constructor it fails in Direct-mode address resolution. Audit of the other providers recorded above: no Gateway analogue exists anywhere else, but MongoDB has the same shape of gap (TASK-225).
- step 4 — layer: local to Birko.Data.CosmosDB.
- step 5 — fix in Birko.Data.CosmosDB/Stores/{Settings.cs, CosmosDBStore.cs, AsyncCosmosDBStore.cs}; tests in Birko.Data.CosmosDB.Tests/CosmosConnectionModeTests.cs (new, 5, non-gated) and BIRKO_COSMOS_CONNECTION_MODE added to the gated matrix suite.
- step 6 — revert (drop ConnectionMode from GetCosmosClientOptions): 1 of 54 failed = The_setting_reaches_the_client_options(mode: Gateway). Contract pins, passing either way: the same theory's Direct case (the default is the SDK's, so it agrees with a broken build — which is exactly why the Gateway case had to be a separate row), The_default_is_unchanged, LoadFrom_carries_the_connection_mode, The_connection_string_constructor_accepts_settings, and the other 49 Cosmos tests. Fixed state: ungated 54/54; gated 53/54, the 54th being the dateDotDate defect filed as TASK-224.
- step 7 — no spec change. This is configuration surface, not translation behaviour; filter-expression-translation already records Cosmos as measured for the shapes this run confirmed, and no area's globs cover Stores/Settings.cs.
- step 8 — closed done; 2ad57be (production) / 326a4ff (tests). Merge gate: builds warning-clean; register-on-introduce — no new cross-cutting pattern, this is one provider gaining a setting its SDK already has, and the audit result (no Gateway analogue elsewhere) is recorded in the task rather than promoted to a CLAUDE.md rule. security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; the setting selects a transport, and the default is unchanged.
