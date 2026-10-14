# Birko.Security.Grants

Per-resource access grants for Birko: owners, editors and viewers of one resource, or of every resource of a type.

## Features

- A validated role → action table; any undeclared role, action or resource type refuses loudly
- Exact grants (one resource) and type-wide grants (every resource of a type)
- Optional tenant binding: a tenant-bound grant never applies outside its tenant
- Grant / revoke rules: share action required, no granting above your own actions, a resource never loses its last owner
- The creator of a resource becomes its owner with one call
- Resource-type visibility (`Granted` / `Public`) for the HTTP layer's 404-vs-403 choice
- Persists through `Birko.Data.Stores` — any backend; no ASP.NET dependency

## Usage

```csharp
using Birko.Security.Grants;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;

public class SqlResourceGrantStore : /* your backend's async bulk store */, IResourceGrantStore { }

var table = new GrantOptions()
    .DefineRole("owner", "read", "edit", "delete", "share")
    .DefineRole("editor", "read", "edit")
    .DefineRole("viewer", "read")
    .ResourceType("presentation", ResourceVisibility.Public)
    .ResourceType("draft")
    .Build();

var grants = new GrantService(store, table, new SystemDateTimeProvider());

await grants.GrantOwnerOnCreateAsync(creator, "presentation", presentationId);
await grants.GrantAsync(creator, colleague, "presentation", presentationId, "editor");

if (!await grants.CanAsync(colleague, "presentation", presentationId, "edit"))
    return Results.Forbid();
```

Refusals throw `GrantRefusedException` with `Code` from `GrantErrorCodes`; configuration errors throw
`GrantConfigurationException`.

### Invitations and the first admin

```csharp
var signInOptions = new GrantSignInOptions();
signInOptions.BootstrapAdmins.Add("github:4821337");          // provider:subject — never an email

var invitations = new InvitationService(invitationStore, grants, table, clock, signInOptions);
var admins = new AdminGrantService(store, clock);
var signIn = new GrantSignInService(invitationStore, invitations, admins, clock, signInOptions);

// An owner invites someone who has not signed in yet; send created.Token in a link yourself.
var created = await invitations.CreateAsync(owner, "jana@example.com", "presentation", presentationId, "editor");

// At every sign-in, after mapping (provider, subject) to your local user:
var result = await signIn.OnSignInAsync(new SignInContext(
    localUserGuid, "github", subject, email,
    EmailVerified: emailVerifiedFromProvider,   // the provider's flag — NOT UserLogin.IsVerified
    InvitationToken: tokenFromLink));
```

## Running tests

```
dotnet test tests/Birko.Security.Grants.Tests --nologo
```

## License

See the repository root `LICENSE`.
