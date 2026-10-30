---
id: TASK-800
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-30
depends-on: [TASK-778]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Sign in with Apple

## Context

Apple is an OpenID Connect provider, so TASK-778's `id_token` check applies: RS256 keys published at
`https://appleid.apple.com/auth/keys`, issuer `https://appleid.apple.com`, audience = the Services ID. Three things
`OpenIdSignIn` cannot do yet:

- **The client secret is a JWT the app signs** with an ES256 key from its Apple developer account (key id, team id,
  Services ID), valid at most six months. The secret has to be generated, and renewed before it expires, not
  configured once.
- **Asking for name or email requires `response_mode=form_post`**: the callback arrives as a POST, not a query
  string. The user's name is sent only on the first sign-in, in a `user` form field, and is never in the `id_token`.
- **Claim quirks**: `email_verified` (and `is_private_email`) may be the strings `"true"` / `"false"`, and the email
  may be a private relay address.

Not filed for a consumer yet. It is commonly required beside Google in consumer-facing apps.

## Acceptance criteria

- [ ] `AppleOpenIdProvider.Create(servicesId, teamId, keyId, privateKey (PEM / ECDsa), redirectUri, scope)`.
- [ ] The client secret is generated on demand: ES256, `iss` = team id, `sub` = Services ID, `aud` =
      `https://appleid.apple.com`, a short lifetime. It is cached and renewed before expiry on the injected clock.
- [ ] A form-post callback: `OpenIdCallback` (or the renamed contract, TASK-798) carries the POSTed `state`, `code`,
      `error` and the `user` JSON. The first sign-in's name reaches the identity, and later sign-ins work without it.
- [ ] `EmailVerified` accepts Apple's string form (already true for `email_verified`); expose `is_private_email` if
      the identity grows a place for it.
- [ ] Tests against a fake Apple: the generated secret verifies with the public key and has the right claims; the
      form-post round trip; the first sign-in with a name, a later one without; the refusal paths shared with TASK-778.
- [ ] Docs: the project's README / CLAUDE.md, `docs/communication.md`.

## Human test plan

A real Apple sign-in from a consumer with a registered Services ID and key: the first sign-in returns the name, a
second sign-in returns the same subject without it. Needs an Apple developer account and an https redirect URI
(Apple refuses localhost).
