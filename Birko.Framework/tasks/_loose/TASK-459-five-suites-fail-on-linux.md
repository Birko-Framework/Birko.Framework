---
id: TASK-459
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

[[TASK-457]] added `build-and-test.yml`, which is the **first CI this framework has ever had** beyond
`token-parity.yml`. The old polyrepo had no build or test workflow at all, so **166 of the 167 test
projects had never run on anything but Windows** — seven years of Windows-only development and
testing.

The first full Linux run was **162 of 167 suites green**. The five below are genuine cross-platform
findings, not migration fallout: **all five pass locally on Windows**, verified immediately after the
CI run.

(A sixth suite, `Birko.DesignTokens.Tests`, also failed and is *not* in scope: its 19 failures are
environmental — `CssParityTests` compares against `Birko.Web.Components/css/` in the other repo, and
`token-parity.yml` is the only workflow that assembles `BIRKO_SRC` plus a `Birko.Web` checkout. It is
now skipped in `build-and-test.yml` and covered there, where it passes.)

## The failures

| Suite | Test | Note |
|---|---|---|
| `Birko.Communication.REST.Tests` | `RestClientCacheTests.GetClient_ConcurrentAccess_DoesNotCorruptCache` | Concurrency test on a 2-core runner. Could be a real race the Windows scheduler hides, or a test that assumes more parallelism than a runner gives. **Do not assume "just flaky" — decide which.** |
| `Birko.Data.Migrations.CosmosDB.Tests` | `DegradedFilterRefusalTests.An_ordinary_filter_is_not_refused` (3 failed in the suite) | Only one distinct name surfaced in the log; confirm whether the other two are the same test in sibling classes. |
| `Birko.Security.AzureKeyVault.Tests` | `AzureKeyVaultSecretProviderTests.ListSecretsAsync_MalformedSecretId_SkipsEntryWithoutThrowing` | A malformed-URI path. URI/path parsing is a classic Windows/Linux divergence. |

## Acceptance

1. **Reproduce each on Linux before changing anything** — Docker with the `mcr.microsoft.com/dotnet/sdk`
   image is enough; do not reason from the Windows result.
2. **For each, decide which of two things it is, and say which**: a product defect that Windows hides,
   or a test that encodes a Windows assumption. The remedies are opposite — fix the code, or fix the
   test — and this file's § Conventions records repeatedly that picking the wrong one ships a narrower
   bug.
3. The concurrency one specifically: a test that passes because a scheduler happens to serialise it is
   not passing for a reason. Establish whether `RestClientCache` has a real race.
4. Green `build-and-test` on Linux without weakening an assertion to get there. If a test genuinely
   cannot hold on Linux, skip it **with a stated reason**, not silently.
