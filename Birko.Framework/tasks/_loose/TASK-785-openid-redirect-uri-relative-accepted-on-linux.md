---
id: TASK-785
parent: null
status: done
priority: P1
assignee: ai
created: 2026-10-29
depends-on: [TASK-778]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# OpenID RedirectUri: a relative path was accepted on Linux (CI red since TASK-778)

## Context

Asked by the user (2026-10-29): "why ci build failed?"

`build-and-test` failed on every push since TASK-778 (first run 40671409051, 2026-10-28), always on one test:
`ProviderOptionsTests.Options_that_cannot_work_are_refused(what: "redirect", value: "/relative")`. It passes on Windows.

Cause: `OpenIdProviderOptions.Validate` checked `RedirectUri` with `Uri.TryCreate(value, UriKind.Absolute, out _)`
only. On Linux .NET parses `"/relative"` as the absolute `file:///relative`, so the check succeeded and nothing
threw. Not a test artefact: a host on Linux with `RedirectUri = "/callback"` passed validation and failed only at the
provider, at sign-in.

## Acceptance criteria

- [x] `RedirectUri` must be an absolute `http` or `https` URL (`http` kept for localhost development); the message
      says so
- [x] Regression cases `file:///relative` and `ftp://app.test/cb`, which fail on Windows too without the fix
      (measured: 2 failing without it, 63/63 with it)
- [x] The suite passes on Linux (`mcr.microsoft.com/dotnet/sdk:10.0`), where the original case failed

## Human test plan

N/A — fully covered by automated tests (a validation rule, run on both Windows and Linux).
