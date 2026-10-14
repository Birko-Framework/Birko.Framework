# Birko.Security.Grants.AspNetCore

ASP.NET Core integration for [Birko.Security.Grants](../Birko.Security.Grants/README.md): a `RequireGrant` endpoint
filter, a permission resolver that surfaces grants through `ICurrentUser`, and one-call DI.

## Features

- `RequireGrant("presentation", "edit")`: per-resource authorization on a Minimal API endpoint; its type, actions and
  route parameter are validated when the endpoints are built
- The **visibility rule** for denials: **403** when the caller may read the resource, **404** when they may not, so a
  private resource's existence never leaks, and a public one the caller can see is never "not found"
- `GrantAwareUserPermissionResolver`: admin grant → `*`, type-wide grants → `{type}:{action}`, through the existing
  `PermissionResolutionMiddleware`
- `AddBirkoGrants(...)`: the role table, grants, invitations, admins and sign-in services in one call

## Usage — external login, end to end

The external provider proves **who** someone is; grants decide **what** they may do. The step consumers get wrong is
the subject: `PermissionResolutionMiddleware` parses the user-id claim as a `Guid`, so a token carrying the provider's
id fails one of two ways. GitHub's numeric id and Google's opaque `sub` don't parse at all, and `RequireGrant`
answers 401 `Grant.UnmappedSubject`. A provider whose subject **happens to be a GUID** (Keycloak, Cognito, Entra)
parses fine but names nobody in your grant store, so every write is a silent 403 or 404. **Either way the fix is
the same: map the provider identity to your own user, and issue your own token whose subject is that user's Guid.**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Your own JWT is the API's credential (Birko.Security.AspNetCore). Permissions are resolved per request,
//    so a revoke works on the next one; WithGrantPermissions below needs your resolver registered first.
builder.Services.AddBirkoSecurity(o => { o.Jwt.Secret = /* … */; o.Jwt.Issuer = "my-app"; o.Jwt.Audience = "my-app"; });
builder.Services.UseResolvedPermissions();
builder.Services.AddScoped<IUserPermissionResolver, MyRolePermissionResolver>();   // your global roles → permissions

// 2. Grant stores on your backend (any Birko async bulk store implementing the two interfaces).
builder.Services.AddSingleton<IResourceGrantStore, SqlResourceGrantStore>();
builder.Services.AddSingleton<IInvitationStore, SqlInvitationStore>();

// 3. Roles, resource types and the first admin.
builder.Services.AddBirkoGrants(
    grants => grants
        .DefineRole("owner", "read", "edit", "delete", "share")
        .DefineRole("editor", "read", "edit")
        .DefineRole("author", "create")                             // type-wide: who may create at all
        .DefineRole("viewer", "read")
        .ResourceType("presentation", ResourceVisibility.Public)   // anyone may read → a denied write is 403
        .ResourceType("draft"),                                     // Granted (default) → no read grant means 404
    signIn => signIn.BootstrapAdmins.Add("google:110248495921238986420"))   // provider:subject — never an email
    .WithGrantPermissions();

// 4. The external login is only a stepping stone: a short-lived cookie scheme, never the API's credential.
builder.Services.AddAuthentication()
    .AddCookie("external", o => o.ExpireTimeSpan = TimeSpan.FromMinutes(5))
    .AddOpenIdConnect("google", o =>
    {
        o.SignInScheme = "external";
        o.Authority = "https://accounts.google.com";
        o.ClientId = /* … */; o.ClientSecret = /* … */;
        o.ResponseType = "code";
        o.Scope.Add("email");
        o.MapInboundClaims = false;   // keep "sub", "email", "email_verified" under their OIDC names
    });

var app = builder.Build();
app.UseAuthentication();
app.UseBirkoPermissionResolution();   // after UseAuthentication

app.MapGet("/signin", () => Results.Challenge(new() { RedirectUri = "/signin-complete" }, ["google"]));

app.MapGet("/signin-complete", async (HttpContext http, MyUserAccounts accounts,
    IGrantSignInService grantSignIn, TokenServiceAdapter tokens) =>
{
    var external = await http.AuthenticateAsync("external");
    if (!external.Succeeded || external.Principal is null)
        return Results.Unauthorized();
    // The cookie has done its job: drop it, so it never becomes a standing credential that mints app tokens.
    await http.SignOutAsync("external");

    const string provider = "google";
    var subject = external.Principal.FindFirstValue("sub");
    if (string.IsNullOrEmpty(subject))
        return Results.Unauthorized();   // never look a login up by a missing key
    var email = external.Principal.FindFirstValue("email");
    // The provider's own statement — NOT UserLogin.IsVerified, which is "always true for OAuth".
    // GitHub has no such claim: read `verified` for the address from GET /user/emails.
    var emailVerified = external.Principal.FindFirstValue("email_verified") == "true";

    // Find or create YOUR user by (provider, subject) — Birko.Models.Users' UserLogin.Provider + ProviderKey —
    // never by email alone.
    var userGuid = await accounts.FindOrCreateAsync(provider, subject, email, emailVerified);

    // Bootstrap admins, then the invitations waiting for this verified email.
    await grantSignIn.OnSignInAsync(new SignInContext(userGuid, provider, subject, email, emailVerified,
        InvitationToken: http.Request.Query["invitation"]));

    // Your own token: sub = YOUR user's Guid.
    var token = tokens.GenerateAccessToken(new TokenRequest(userGuid, email ?? string.Empty));
    return Results.Ok(token);
});

// Reads are anonymous; writes are gated.
app.MapGet("/presentations/{id}", (Guid id) => /* … */ Results.Ok());
app.MapPut("/presentations/{id}", (Guid id) => /* … */ Results.NoContent())
   .RequireGrant("presentation", "edit");
app.MapDelete("/presentations/{id}", (Guid id) => /* … */ Results.NoContent())
   .RequireGrant("presentation", "delete");
app.MapPost("/presentations", async (ICurrentUser user, IGrantService grants) =>
{
    var id = Guid.NewGuid();
    /* … create it … */
    await grants.GrantOwnerOnCreateAsync(user.UserGuid!.Value, "presentation", id, user.TenantGuid);   // creator owns it
    return Results.Created($"/presentations/{id}", null);
}).RequireGrant("presentation", "create", routeParam: null);   // type-wide check: holders of "author" (or an admin)
// Global, non-resource permissions keep using RequirePermission from Birko.Security.AspNetCore.

app.Run();
```

`MyUserAccounts` and `MyRolePermissionResolver` stand for your own code around `Birko.Models.Users` (`User`,
`UserLogin`, roles); their shape is the app's. `RequireGrant` checks its route parameter, type and actions when the
endpoints are built, which is normally the first request, not `app.Run()`.
### What each response means

| Situation | Response |
|---|---|
| Not signed in | 401, decided before any lookup |
| Signed in, but the token's subject isn't a local user Guid | 401 `{ code: "Grant.UnmappedSubject" }` |
| The id in the route isn't a Guid | 404 |
| Allowed | the endpoint runs |
| Denied, `Public` type | 403 `{ required: "presentation:edit" }` |
| Denied, `Granted` type, caller may read it | 403 |
| Denied, `Granted` type, caller may not read it | 404, identical to a missing resource |

### Caveats

- **Last owner and last admin are protected by an in-process lock plus a recount after the write**: a compensation,
  not a transaction. Two instances racing can leave a resource ownerless for a moment, until one of them puts the
  grant back and refuses.
- **Cross-instance single use of an invitation** needs a backend whose filtered property update is one conditional
  statement: SQL, or MongoDB. File and in-memory stores get it within one process only.
- **No caching**: every `RequireGrant` reads the grant store, so a revoke works on the next request.

## Running tests

```
dotnet test tests/Birko.Security.Grants.Tests --nologo
```

## License

See the repository root `LICENSE`.
