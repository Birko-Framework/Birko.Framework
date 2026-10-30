---
id: TASK-798
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-30
depends-on: [TASK-778]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# GitHub sign-in behind the same contract as Google and Microsoft

## Context

TASK-778 gave Google and Microsoft one sign-in service (`IOpenIdSignIn` → `OpenIdIdentity` and a status). GitHub is
plain OAuth, not OpenID Connect: there is no `id_token`, and the user comes from `GET /user` with the access token.
TASK-521's pieces (`GitHubOAuthProvider.CreateWebFlowSettings`, `GitHubUserClient`, `IOAuthStateStore`,
`OAuthCodeExchanger`) are wired by each app itself, and they throw rather than answer a status. An app offering all
three providers therefore handles GitHub differently.

The interface name `IOpenIdSignIn` does not fit a non-OIDC provider. Presenter (TASK-058) is its only consumer, so
renaming it now costs one consumer edit.

## Acceptance criteria

- [ ] `GitHubSignIn` implements the shared contract:
  - `BeginAsync(binding, data)`: browser-bound state, PKCE.
  - `CompleteAsync(callback, binding)`: the same status enum and identity, with `Provider = "github"`,
    `Subject` = the numeric user id (never the login, which can be renamed), `Issuer` = `https://github.com`, `Name`.
    `Email` and `EmailVerified` come from the verified primary address (`GetPrimaryEmailAsync`), and are null / false
    when there is none. `TenantId` is null.
- [ ] The contract is renamed to something provider-neutral (proposal: `IExternalSignIn`, `ExternalIdentity`,
      `ExternalSignInStatus`, …). Decide whether to keep `IOpenIdSignIn` as an alias or break, and record the decision.
- [ ] Placement: GitHub's sign-in must not pull `Microsoft.IdentityModel.JsonWebTokens` into GitHub-only consumers, so
      the contract types probably move to `Birko.Communication.OAuth`. Decide, and record it in both projects' CLAUDE.md.
- [ ] Tests against a fake GitHub (token endpoint answering errors with HTTP 200 as GitHub does, `/user`,
      `/user/emails`): success, another browser's state, a reused state, the error callback, a refused code, no
      verified email, a 5xx.
- [ ] Docs: `docs/communication.md`, both projects' README / CLAUDE.md. Presenter is told about the rename.

## Human test plan

A real GitHub sign-in in a consumer (Presenter or DraCode) through `GitHubSignIn` signs in and stores
`github:<numeric id>`.
