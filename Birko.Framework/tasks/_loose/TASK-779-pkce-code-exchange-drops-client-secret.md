---
id: TASK-779
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-28
depends-on: []
blocks: [TASK-778]
pr: null
github-issue: null
jira-key: null
---

# A PKCE code exchange drops the client secret, so Google and Microsoft refuse it

## Context

Found by the Presenter session while designing TASK-778. `OAuthClient.ExchangeCodeAsync` sent `client_secret` only
when no `codeVerifier` was passed. RFC 7636 adds the verifier to the exchange; it does not replace client
authentication. A confidential web client using PKCE therefore sent no secret, and both Google (`client_secret is
missing`) and Microsoft (`AADSTS7000218`) refuse that. The unit test `ExchangeCodeAsync_WithPkce_SendsCodeVerifier`
asserted the defect ("PKCE doesn't send client_secret").

## Acceptance criteria

- [x] The secret is sent whenever one is configured, with or without a verifier
- [x] A public client (no secret) with PKCE still sends no secret
- [x] The test that asserted the old behaviour is inverted, not deleted; red on the code before

## Human test plan

N/A — fully covered by automated tests. Google's and Microsoft's acceptance is exercised in Presenter TASK-058's
real sign-in (TASK-778's human test).

## Outcome (2026-10-28)

`OAuthClient.ExchangeCodeAsync` sends `client_secret` whenever it is configured. Tests:
- `ExchangeCodeAsync_WithPkce_SendsCodeVerifier` now expects the secret beside the verifier, and fails on the code
  before;
- new `ExchangeCodeAsync_WithPkce_AndNoSecret_SendsNoSecret`.

Regression over every importer of the project: OAuth.Tests 75/75, OAuth.Providers.Tests 21/21, AI.Providers.Tests
246/246, AI.Tests 75/75, AI.Agents.Tests 82/82.
