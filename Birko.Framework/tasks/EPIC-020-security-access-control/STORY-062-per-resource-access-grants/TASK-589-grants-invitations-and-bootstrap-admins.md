---
id: TASK-589
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-13
depends-on: [TASK-588]
blocks: [TASK-590]
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Grants — invitations, bootstrap admins, and the sign-in hook

## Context

Second of three tasks under [[STORY-062]]. It builds on [[TASK-588]]'s `GrantService` and role table, and
the story owns the design (decisions 3, 11, 12).

Two gaps a grant model alone leaves:

1. **Granting to someone who hasn't signed in yet.** An owner invites by email; the grant appears when
   that person first signs in.
2. **The first admin.** A grant needs a granter, so the first one cannot be granted. It comes from
   configuration as a `provider:subject` pair, never from an email and never from "the first user to sign
   up", which is a race anyone can win.

⚠ **The email-verified signal is the provider's `email_verified` claim**, which the host passes into
`SignInContext.EmailVerified` (and may store in `User.EmailVerified`). **`UserLogin.IsVerified` is never
used**: `Birko.Models.Users/Models/UserLogin.cs:50` documents it as "always true for OAuth", so an
unverified Google/GitHub email would redeem an invitation. That is the account-takeover path this task
exists to close. Decided 2026-10-13.

## Acceptance criteria

- [x] `Invitation` model + `IInvitationStore : IAsyncBulkStore<Invitation>`. An invitation is created by a
      granter for `(email, resourceType, resourceId?, role, tenant?)`, with the same authorization as
      `GrantAsync` (share + no escalation). It has an expiry from options (default 7 days, clock via
      `IDateTimeProvider`) and is single use. Only a SHA-256 hash of the token is stored, and the plaintext
      is returned once.
- [x] `OnSignInAsync(SignInContext)` redeems invitations **only when `EmailVerified` is true**, and that
      value comes from the provider claim, not from `UserLogin.IsVerified`.
      - An unverified email redeems nothing and is reported in the result.
      - An explicit token with an unverified email throws `Invitation.EmailNotVerified`.
      - Expired, used and revoked invitations refuse, as do an email mismatch and an inviter who has lost
        `share`.
- [x] Bootstrap admins come from `GrantOptions.BootstrapAdmins` as `provider:subject` pairs. They are
      applied on the matching sign-in as the reserved `*` grant, idempotently: two sign-ins give one grant.
      No email and no "first user" path exists.
      _Delivered as `GrantSignInOptions.BootstrapAdmins` (with `InvitationLifetime`), not `GrantOptions`: the role
      table stays a role table, and the sign-in settings are only read by the sign-in services._
- [x] An admin can be removed through the framework, not only by a raw store delete. `IGrantService.RevokeAsync`
      cannot do it: it requires a declared resource type, and the reserved type `*` can never be declared (found
      by TASK-588's close review, 2026-10-14). Add `RevokeAdminAsync(caller, user, tenant?)`. Only an admin may
      call it, and removing the **last** admin in scope is refused, the same rule as the last owner.
      _Also: anyone may remove their own admin grant (still never the last), as with `RevokeAsync`; a tenant's
      count includes the global admins, who administer every tenant (close review, 2026-10-14)._
- [x] Redemption is safe under concurrency: the same invitation redeemed twice at once produces one grant.
- [x] Tests:
      - invitation verified / unverified / unverified + explicit token / expired / reused / revoked /
        email mismatch / inviter lost share / concurrent double redeem;
      - bootstrap idempotence, and that a non-listed first user gets nothing;
      - token stored only as a hash.

## Out of scope

- Sending the invitation email: the service returns the token, and delivery is the host's job
  (`Birko.Messaging`).
- The external login handler and the `UserLogin` lookup: host code, shown in [[TASK-590]]'s README.
- Revoking a bootstrap admin automatically when they are removed from config. That is a deliberate limit, recorded
  as a tradeoff in the story: an admin calls `IAdminGrantService.RevokeAdminAsync`, which this task adds, and
  the identity must also leave `BootstrapAdmins` or the next sign-in writes the grant again.

## Human test plan

N/A — fully covered by automated tests. The sign-in flow against a real provider is checked end-to-end
in [[TASK-590]]'s human test plan.

## Implementation plan

Design: [[STORY-062]] § Design, decisions 3, 11, 12. Steps:

1. **Model and store:** `Models/Invitation.cs` and `Stores/IInvitationStore.cs`, with `GetByTokenHashAsync`
   and `ListPendingForEmailAsync(email, now)`.
2. **Token hashing:** `Internal/TokenHasher.cs`. The token is 32 bytes from `RandomNumberGenerator` →
   base64url, and the stored form is SHA-256 hex. Compare in fixed time.
3. **Invitation service:** `IInvitationService` / `InvitationService` with Create, which returns
   `InvitationCreated { Invitation, Token }`, plus Revoke and RedeemByToken. Each redemption runs under the
   keyed lock from TASK-588, re-reads the invitation, checks it, grants with `GrantedBy = InvitedBy`, and
   marks it redeemed.
4. **Sign-in:** `SignInContext` / `SignInResult` (with `Skipped`) and `IGrantSignInService.OnSignInAsync`.
   Bootstrap runs first, then redemption by normalized email (trim, lower-invariant), then the explicit
   token if one was passed.
5. **Options:** `GrantOptions.BootstrapAdmins` and `InvitationLifetime`.
6. **Tests:** as in the acceptance criteria. Use `TestDateTimeProvider` for expiry and `Task.WhenAll` for
   the double redeem.
7. **Finish:** update the project's `CLAUDE.md` / `README.md`. Run the tests and
   `/verify-birko-conventions`. One commit.
