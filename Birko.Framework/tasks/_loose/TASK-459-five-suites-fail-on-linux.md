---
id: TASK-459
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-457]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Five tests fail on Linux that pass on Windows

## Context

[[TASK-457]] added `build-and-test.yml`, the **first CI this framework has ever had** beyond
`token-parity.yml`. The old polyrepo had no build or test workflow, so **166 of the 167 test projects
had never run on anything but Windows** — seven years of Windows-only development.

First full Linux run: **162 of 167 suites green.** The failures were three different things and
needed three different answers.

(`Birko.DesignTokens.Tests` also failed, 19 tests, and is *not* part of this: its failures are
environmental — `CssParityTests` compares against `Birko.Web.Components/css/` in the other repo, and
`token-parity.yml` is the only workflow that assembles `BIRKO_SRC` plus a `Birko.Web` checkout, where
it passes. Now skipped in `build-and-test.yml` with the reason in place.)

## 1. Product defect that Windows hides — FIXED

`Birko.Security.AzureKeyVault` · `ExtractSecretName` / `ExtractVersion`

`Uri.TryCreate(id, UriKind.Absolute, …)` is not a check for "is this a secret id", and what it accepts
is **platform-dependent**. Measured on .NET 10:

| input | Windows | Linux |
|---|---|---|
| `/secrets/relative-only` | `false` — skipped | **`true`, scheme `file`, 3 segments** |

So on Linux a malformed id yields the secret name `relative-only` instead of being skipped. CR-L340's
original fix read as correct for exactly one reason: it had only ever been executed on Windows — and
**the framework deploys in Linux containers, so the wrong answer was the one that shipped.**

Fixed with `TryParseSecretId`, which additionally requires an http/https scheme (http only because
test doubles and emulators use it).

**The pre-existing test could not have caught this on Windows**, so a new one was added
(`ListSecretsAsync_NonHttpSecretId_IsSkipped`) that states the rule directly with `file:` and `ftp:`
ids. Mutation-verified on Windows: removing the scheme check reds exactly that test, 1 of 28.

## 2. Test bug — FIXED

`Birko.Data.Migrations.CosmosDB.Tests` · `DegradedFilterRefusalTests.ShouldNotBeRefused` (3 tests)

`Task.Wait(timeout)` **rethrows** as `AggregateException` when the task has already faulted; it only
returns a bool when the task is still running or completed cleanly. The helper's intent is *"a
connection error is fine, a refusal is not"*, and it inspects the exception on the line below — but
the `Wait` threw first.

On Windows the unreachable endpoint never resolves inside the 2-second grace period, so the timeout
branch was always taken and the rethrow was **unreachable**. On Linux the connection is refused
immediately (`Connection refused (localhost:1)`), the task faults in milliseconds, and the throw
escaped as a failure — reporting a *connection* error as though the guard had misbehaved.

Fixed by catching the `AggregateException` and falling through to the existing inspection, which
preserves the intent exactly. The product code was never at fault.

## 3. Tests encoding Windows assumptions — FIXED

`Birko.Helpers.Tests` (3 tests). Found only after the `.trx` instrumentation below existed: the
first two runs reported this suite as `Failed: 3` with **no test names at all**, because `-v q`
suppressed them. The artifact gave both names and messages on the next run.

| test | why it failed on Linux |
|---|---|
| `IsUnderDirectory_HandlesContainmentAndSiblingPrefix` ×2 | data was `@"C:\base\sub\file.txt"`; the helper compares against `Path.DirectorySeparatorChar`, which is `/` on Unix, so that string is one filename containing backslashes and is not under `C:\base` |
| `ValidateUserPath_AbsolutePath_Throws` | data was `"C:\Windows\System32"`; the validator asks `Path.IsPathRooted`, and a drive letter means nothing on Unix, so nothing was thrown |

**The product is correct in both cases** — `PathHelper` and `PathValidator` are deliberately
platform-aware, and they behaved correctly on each platform. The test *data* was Windows-only.
Rebuilt at runtime from `Path.DirectorySeparatorChar` and `OperatingSystem.IsWindows()`, keeping
every original case. A companion test pins the other side of the switch (on Unix a drive-letter
string is not rooted and is accepted) so that reads as a decision rather than a gap; traversal is
still refused by the `..` check on both platforms, which is what carries the security property.

## 4. A test-isolation defect the attribute was meant to prevent — FIXED

`Birko.Communication.REST.Tests` · `RestClientCacheTests.GetClient_ConcurrentAccess_DoesNotCorruptCache`

Failed **2 of 4 CI runs** and never once locally — 5 clean Linux runs including CPU-limited, plus
green on Windows. The first two failures carried **no assertion message**, because the workflow ran
`-v q`. The `.trx` artifact added for exactly this purpose answered it on the next occurrence:

> `Expected seen.Distinct() to contain 40 item(s), but found 80`

**Exactly double**, which is not contention noise — it is the cache being cleared **once**, mid-run,
so the same 40 URIs resolved to fresh instances on the far side of the eviction.

`RestClient._clients` is `static`. `RestClientCacheTests` already carried
`[Collection("RestClientCache")]` with the comment *"avoid interleaving with other tests that touch
the static cache"* — but **xUnit serialises classes WITHIN a collection and runs different
collections in PARALLEL**, so naming it on one of the two classes serialised nothing.
`RestClientTests.ClearCache_EvictsAllEntries` was free to run concurrently and wipe the cache. The
author's intent was right and its implementation reached one of the two places that needed it — the
shape this file records repeatedly as *a rule enforced in one of two places*.

Fixed by putting `RestClientTests` in the same collection. **The product was never at fault**, and the
earlier reading holds: `GetOrAdd`'s factory may run twice, but every caller still receives the stored
instance, so the assertion was always sound. The `Lazy<T>` remedy was **not** applied — it would have
been a plausible-looking fix for a cause that was not the cause.

**A behavioural test cannot guard this** (the interleaving has to genuinely occur), so the guard is a
source scan: every class touching the static cache must name the collection. Mutation-verified —
removing the attribute reds exactly that test.

> ⚠ The first version of that scan **failed its own mutation, 0 tests red**. These files explain the
> defect at length, so their prose contains both the forbidden calls and the required attribute, and
> scanning raw text let a comment satisfy the requirement. It now strips comment lines first. This
> repo already records the same trap for `SqlitePoolIsolationTests` ("a guard that must NAME the
> thing it forbids has to assemble the name, or it reports itself") — here it arrived from the other
> side, through the string the guard *requires* rather than the one it forbids.

## 5. The same isolation defect again, in gRPC — FIXED

`Birko.Communication.gRPC.Tests` · `GrpcClientFactoryTests.CreateClient_From_Settings_Uses_Pooled_Channel`

Surfaced on the run *after* §4 was fixed, with `ObjectDisposedException: GrpcChannel` — and it is
§4's defect exactly. `GrpcChannelPool._channels` is `static`; `GrpcChannelPoolTests` **disposes every
pooled channel** in both its constructor and `Dispose`, and carried `[Collection("ChannelPool")]`
while `GrpcClientFactoryTests` did not. So the disposal ran in parallel with a test legitimately
holding a channel.

**Two accidental discoveries of one shape is a reason to sweep, not to wait for CI.** A scan of all
167 test projects for *partial* collection coverage — some classes in a collection, others not —
returned **exactly these two** and nothing else, so the pattern is now closed rather than merely
twice-patched. (REST's third class and gRPC's other three genuinely do not touch the shared state;
their own source scans assert it.)

Guarded the same way, with comment-stripping from the start this time. Mutation-verified: removing
the attribute reds exactly that test.

## Outcome

**All six fixed: one product defect, five test defects.** Worth stating because the instinct on
seeing "5 tests fail on Linux" is to assume the code is wrong; it was wrong once, and that once was
the one that ships to production.

Kept from this: `build-and-test.yml` writes a `.trx` per suite and uploads it on failure. Two of the
four findings here were diagnosed only from that artifact, having been invisible — one entirely
nameless — in the console log.
