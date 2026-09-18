---
id: TASK-459
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
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

## 4. Intermittent, NOT reproduced — INSTRUMENTED, still open

`Birko.Communication.REST.Tests` · `RestClientCacheTests.GetClient_ConcurrentAccess_DoesNotCorruptCache`

Failed **once** in CI and has not reproduced: 5 clean runs in a Linux container, including
CPU-limited, plus green on Windows. **No assertion message was captured**, because the workflow ran
`dotnet test -v q`, which suppresses the detail.

Ruled out by reading, not by guessing: `ConcurrentDictionary.GetOrAdd(key, factory)` may invoke the
factory more than once under contention, but `TryAddInternal` returns the value **actually in the
dictionary**, so every caller still receives the same instance and the test's
`seen.Distinct().Should().HaveCount(40)` assertion is sound. What that does leave is discarded
`RestClient` instances, each owning an undisposed `HttpClientHandler` + `HttpClient` — a plausible
resource-pressure story on a 2-core runner, but **unverified and not to be treated as the cause
without evidence**.

**What was done instead of a speculative fix:** `build-and-test.yml` now writes a `.trx` per suite and
uploads them as an artifact on failure, so the next occurrence carries its assertion text and stack.

## Remaining acceptance

1. When it recurs, read the `.trx` from the run artifact before changing anything.
2. Then decide which it is: a real race, resource exhaustion from `GetOrAdd`'s discarded instances, or
   a test that assumes more parallelism than a runner provides. **The remedies are opposite** — this
   file's § Conventions records repeatedly that picking the wrong one ships a narrower bug.
3. If it turns out to be the discarded instances, the fix is `ConcurrentDictionary<string,
   Lazy<RestClient>>` with `LazyThreadSafetyMode.ExecutionAndPublication`, which constructs exactly
   once per key. Do not apply that pre-emptively — it is a guess until the `.trx` says so.
4. Do not weaken the assertion to get green.
