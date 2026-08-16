---
id: TASK-223
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-220, TASK-222]
pr: null
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

- [ ] A consumer can select Gateway mode through `Settings`, and the default is unchanged (`Direct`)
- [ ] `CosmosFilterMatrixLiveTests` runs against the Docker emulator and its **full shape report** is
      recorded in this task — the report is the point, not the pass
- [ ] Any divergence it reveals is fixed or filed; none left unexplained
- [ ] A non-gated test pins that `GetCosmosClientOptions()` honours the setting
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- [[TASK-222]]'s normalizer work. Cosmos was measured during this investigation to honour ternary,
  coalesce and arithmetic, and that finding is already recorded in `ExpressionNormalizer`'s comment.

## Human test plan

- [ ] `docker run --rm -p 8081:8081 mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview --protocol http`,
      wait for *"System is now fully ready to accept requests"*, then run the suite with
      `BIRKO_COSMOS_CONNECTION` set and read the whole report.

## Implementation plan

_Populated by `/tasks plan TASK-223` — leave empty until then._
