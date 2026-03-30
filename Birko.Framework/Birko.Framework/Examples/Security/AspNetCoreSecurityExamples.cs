using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Birko.Security;
using Birko.Security.AspNetCore;

namespace Birko.Framework.Examples.Security
{
    /// <summary>
    /// Examples demonstrating the Birko.Security.AspNetCore framework:
    /// JWT Bearer setup, ICurrentUser, permission checking, tenant resolution, and one-line DI.
    /// </summary>
    public static class AspNetCoreSecurityExamples
    {
        /// <summary>
        /// Demonstrates ICurrentUser claim extraction from a ClaimsPrincipal.
        /// </summary>
        public static void RunCurrentUserExample()
        {
            ExampleOutput.WriteLine("=== ICurrentUser Example ===\n");

            // Simulate an authenticated user with JWT claims
            var userId = Guid.NewGuid();
            var tenantGuid = Guid.NewGuid();
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Email, "john@example.com"),
                new(JwtClaimNames.TenantGuid, tenantGuid.ToString()),
                new(ClaimTypes.Role, "Admin"),
                new(ClaimTypes.Role, "User"),
                new(JwtClaimNames.Permission, "users.read"),
                new(JwtClaimNames.Permission, "users.write"),
                new(JwtClaimNames.Permission, "orders.read")
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            // Create ICurrentUser from claims (normally injected via DI)
            var httpContext = new DefaultHttpContext { User = principal };
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            var options = new ClaimMappingOptions(); // defaults: NameIdentifier, Email, Role, Permission
            ICurrentUser currentUser = new ClaimsCurrentUser(accessor, options);

            ExampleOutput.WriteLine("ClaimsCurrentUser properties:");
            ExampleOutput.WriteLine($"  IsAuthenticated: {currentUser.IsAuthenticated}");
            ExampleOutput.WriteLine($"  UserId: {currentUser.UserId}");
            ExampleOutput.WriteLine($"  Email: {currentUser.Email}");
            ExampleOutput.WriteLine($"  TenantGuid: {currentUser.TenantGuid}");
            ExampleOutput.WriteLine($"  Roles: [{string.Join(", ", currentUser.Roles)}]");
            ExampleOutput.WriteLine($"  Permissions: [{string.Join(", ", currentUser.Permissions)}]");

            // GetClaim for arbitrary claims
            ExampleOutput.WriteLine($"\n  GetClaim(\"email\"): {currentUser.GetClaim(ClaimTypes.Email)}");
            ExampleOutput.WriteLine($"  GetClaim(\"nonexistent\"): {currentUser.GetClaim("nonexistent") ?? "(null)"}");

            // Custom claim mapping
            ExampleOutput.WriteLine("\nClaimMappingOptions (configurable claim types):");
            ExampleOutput.WriteLine($"  UserIdClaim: {options.UserIdClaim}");
            ExampleOutput.WriteLine($"  EmailClaim: {options.EmailClaim}");
            ExampleOutput.WriteLine($"  TenantGuidClaim: {options.TenantGuidClaim}");
            ExampleOutput.WriteLine($"  RoleClaim: {options.RoleClaim}");
            ExampleOutput.WriteLine($"  PermissionClaim: {options.PermissionClaim}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Demonstrates ClaimsPermissionChecker with wildcard support.
        /// </summary>
        public static async Task RunPermissionCheckerExample()
        {
            ExampleOutput.WriteLine("=== Permission Checker Example ===\n");

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            // Regular user with specific permissions
            var regularUser = CreateCurrentUser(userId, "user@example.com",
                roles: new[] { "User" },
                permissions: new[] { "orders.read", "orders.create" });

            var checker = new ClaimsPermissionChecker(regularUser);

            ExampleOutput.WriteLine("Regular user permissions: [orders.read, orders.create]");
            ExampleOutput.WriteLine($"  HasPermission(orders.read): {await checker.HasPermissionAsync(userId, "orders.read")}");
            ExampleOutput.WriteLine($"  HasPermission(orders.delete): {await checker.HasPermissionAsync(userId, "orders.delete")}");
            ExampleOutput.WriteLine($"  HasPermission(wrong user): {await checker.HasPermissionAsync(otherUserId, "orders.read")}");

            var permissions = await checker.GetPermissionsAsync(userId);
            ExampleOutput.WriteLine($"  GetPermissions: [{string.Join(", ", permissions)}]");

            // Superadmin with wildcard
            ExampleOutput.WriteLine("\nSuperadmin with wildcard \"*\" permission:");
            var superadmin = CreateCurrentUser(userId, "admin@example.com",
                roles: new[] { "Superadmin" },
                permissions: new[] { "*" });

            var adminChecker = new ClaimsPermissionChecker(superadmin);
            ExampleOutput.WriteLine($"  HasPermission(anything.at.all): {await adminChecker.HasPermissionAsync(userId, "anything.at.all")}");
            ExampleOutput.WriteLine($"  HasPermission(users.delete): {await adminChecker.HasPermissionAsync(userId, "users.delete")}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Demonstrates TokenServiceAdapter for structured token generation and validation.
        /// </summary>
        public static void RunTokenAdapterExample()
        {
            ExampleOutput.WriteLine("=== Token Service Adapter Example ===\n");

            var secret = "ThisIsAVeryLongSecretKeyForHmacSha256SigningAtLeast32Bytes!";

            // JwtAuthenticationOptions configures the entire JWT pipeline
            var authOptions = new JwtAuthenticationOptions
            {
                Secret = secret,
                Issuer = "BirkoFramework",
                Audience = "BirkoApp",
                ExpirationMinutes = 60,
                ClockSkewSeconds = 60
            };

            ExampleOutput.WriteLine("JwtAuthenticationOptions:");
            ExampleOutput.WriteLine($"  Issuer: {authOptions.Issuer}");
            ExampleOutput.WriteLine($"  Audience: {authOptions.Audience} (EffectiveAudience: {authOptions.EffectiveAudience})");
            ExampleOutput.WriteLine($"  Expiration: {authOptions.ExpirationMinutes} min, ClockSkew: {authOptions.ClockSkewSeconds} sec");

            try
            {
                // TokenServiceAdapter wraps ITokenProvider with structured records
                var tokenOptions = new TokenOptions
                {
                    Secret = secret,
                    Issuer = authOptions.Issuer,
                    Audience = authOptions.EffectiveAudience,
                    ExpirationMinutes = authOptions.ExpirationMinutes
                };
                var provider = new Birko.Security.Jwt.JwtTokenProvider(tokenOptions);
                var adapter = new TokenServiceAdapter(provider, authOptions);

                // Generate token with structured TokenRequest
                var userId = Guid.NewGuid();
                var tenantGuid = Guid.NewGuid();
                var request = new TokenRequest(
                    UserId: userId,
                    Email: "john@example.com",
                    TenantGuid: tenantGuid,
                    Roles: new HashSet<string> { "Admin", "User" }.AsReadOnly(),
                    Permissions: new HashSet<string> { "users.read", "users.write" }.AsReadOnly());

                ExampleOutput.WriteLine("\nTokenRequest:");
                ExampleOutput.WriteLine($"  UserId: {request.UserId}");
                ExampleOutput.WriteLine($"  Email: {request.Email}");
                ExampleOutput.WriteLine($"  TenantGuid: {request.TenantGuid}");
                ExampleOutput.WriteLine($"  Roles: [{string.Join(", ", request.Roles!)}]");
                ExampleOutput.WriteLine($"  Permissions: [{string.Join(", ", request.Permissions!)}]");

                var tokenResult = adapter.GenerateAccessToken(request);
                ExampleOutput.WriteLine($"\nGenerated token: {tokenResult.Token[..60]}...");
                ExampleOutput.WriteLine($"Expires at: {tokenResult.ExpiresAt:u}");

                // Validate and get structured TokenValidationInfo
                var info = adapter.ValidateToken(tokenResult.Token);
                ExampleOutput.WriteLine($"\nTokenValidationInfo:");
                ExampleOutput.WriteLine($"  IsValid: {info.IsValid}");
                ExampleOutput.WriteLine($"  UserId: {info.UserId}");
                ExampleOutput.WriteLine($"  Email: {info.Email}");
                ExampleOutput.WriteLine($"  TenantGuid: {info.TenantGuid}");
                ExampleOutput.WriteLine($"  Roles: [{string.Join(", ", info.Roles!)}]");
                ExampleOutput.WriteLine($"  Permissions: [{string.Join(", ", info.Permissions!)}]");

                // Refresh token
                var refreshToken = adapter.GenerateRefreshToken();
                ExampleOutput.WriteLine($"\nRefresh token: {refreshToken}");

                // Invalid token
                var invalid = adapter.ValidateToken("not-a-valid-token");
                ExampleOutput.WriteLine($"\nInvalid token validation: IsValid={invalid.IsValid}");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"\nJWT operations require System.IdentityModel.Tokens.Jwt: {ex.Message}");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Demonstrates tenant resolution strategies (Header and Subdomain).
        /// </summary>
        public static async Task RunTenantResolverExample()
        {
            ExampleOutput.WriteLine("=== Tenant Resolver Example ===\n");

            // --- Header-based resolution ---
            ExampleOutput.WriteLine("1. HeaderTenantResolver");
            var headerResolver = new HeaderTenantResolver();

            var tenantGuid = Guid.NewGuid();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Tenant-Id"] = tenantGuid.ToString();
            httpContext.Request.Headers["X-Tenant-Name"] = "Acme Corp";

            var result = await headerResolver.ResolveAsync(httpContext);
            ExampleOutput.WriteLine($"  Headers: X-Tenant-Id={tenantGuid}, X-Tenant-Name=Acme Corp");
            ExampleOutput.WriteLine($"  Resolved: TenantGuid={result?.TenantGuid}, Name={result?.TenantName}");

            // Missing header
            var emptyContext = new DefaultHttpContext();
            var noResult = await headerResolver.ResolveAsync(emptyContext);
            ExampleOutput.WriteLine($"  No headers: {(noResult == null ? "null (correct)" : "unexpected")}");

            // --- Subdomain-based resolution ---
            ExampleOutput.WriteLine("\n2. SubdomainTenantResolver");

            // Simulate tenant database lookup
            var tenants = new Dictionary<string, Guid>
            {
                ["acme"] = Guid.NewGuid(),
                ["contoso"] = Guid.NewGuid()
            };

            var subdomainResolver = new SubdomainTenantResolver(
                lookupAsync: (subdomain, ct) =>
                {
                    if (tenants.TryGetValue(subdomain, out var id))
                        return Task.FromResult<TenantInfo?>(new TenantInfo(id, subdomain));
                    return Task.FromResult<TenantInfo?>(null);
                },
                baseDomain: "myapp.com");

            // Resolve known tenant
            var subCtx = new DefaultHttpContext();
            subCtx.Request.Host = new HostString("acme.myapp.com");
            var subResult = await subdomainResolver.ResolveAsync(subCtx);
            ExampleOutput.WriteLine($"  Host: acme.myapp.com → TenantGuid={subResult?.TenantGuid}, Name={subResult?.TenantName}");

            // Resolve unknown tenant
            var unknownCtx = new DefaultHttpContext();
            unknownCtx.Request.Host = new HostString("unknown.myapp.com");
            var unknownResult = await subdomainResolver.ResolveAsync(unknownCtx);
            ExampleOutput.WriteLine($"  Host: unknown.myapp.com → {(unknownResult == null ? "null (not found)" : "unexpected")}");

            // No subdomain
            var bareCtx = new DefaultHttpContext();
            bareCtx.Request.Host = new HostString("myapp.com");
            var bareResult = await subdomainResolver.ResolveAsync(bareCtx);
            ExampleOutput.WriteLine($"  Host: myapp.com → {(bareResult == null ? "null (no subdomain)" : "unexpected")}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Demonstrates the PermissionEndpointFilter for Minimal API authorization.
        /// </summary>
        public static async Task RunEndpointFilterExample()
        {
            ExampleOutput.WriteLine("=== Permission Endpoint Filter Example ===\n");

            ExampleOutput.WriteLine("PermissionEndpointFilter checks ICurrentUser.Permissions on each request.");
            ExampleOutput.WriteLine("Usage in Minimal API:");
            ExampleOutput.WriteLine("  app.MapGet(\"/admin/users\", handler).RequirePermission(\"users.read\");");
            ExampleOutput.WriteLine("  app.MapDelete(\"/admin/users/{id}\", handler).RequirePermission(\"users.delete\");\n");

            // Simulate filter with authorized user
            var filter = new PermissionEndpointFilter("users.read");
            EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("Success: endpoint reached");

            // Authorized request
            var authorizedUser = CreateCurrentUser(Guid.NewGuid(), "admin@example.com",
                permissions: new[] { "users.read", "users.write" });
            var authCtx = CreateEndpointContext(authorizedUser);
            var authResult = await filter.InvokeAsync(authCtx, next);
            ExampleOutput.WriteLine($"Authorized (has users.read): {authResult}");

            // Unauthorized request (missing permission)
            var limitedUser = CreateCurrentUser(Guid.NewGuid(), "viewer@example.com",
                permissions: new[] { "reports.read" });
            var forbidCtx = CreateEndpointContext(limitedUser);
            var forbidResult = await filter.InvokeAsync(forbidCtx, next);
            ExampleOutput.WriteLine($"Forbidden (lacks users.read): {forbidResult?.GetType().Name ?? "null"}");

            // Unauthenticated request (no ICurrentUser registered)
            var noAuthServices = new ServiceCollection().BuildServiceProvider();
            var unauthCtx = new DefaultEndpointFilterInvocationContext(
                new DefaultHttpContext { RequestServices = noAuthServices });
            var unauthResult = await filter.InvokeAsync(unauthCtx, next);
            ExampleOutput.WriteLine($"Unauthenticated (no user): {unauthResult?.GetType().Name ?? "null"}");

            // Wildcard superadmin
            var superadmin = CreateCurrentUser(Guid.NewGuid(), "superadmin@example.com",
                permissions: new[] { "*" });
            var superCtx = CreateEndpointContext(superadmin);
            var superResult = await filter.InvokeAsync(superCtx, next);
            ExampleOutput.WriteLine($"Superadmin (wildcard \"*\"): {superResult}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Demonstrates AddBirkoSecurity() one-line DI registration options.
        /// </summary>
        public static void RunDiRegistrationExample()
        {
            ExampleOutput.WriteLine("=== AddBirkoSecurity() DI Example ===\n");

            ExampleOutput.WriteLine("One-line setup registers all security services:\n");
            ExampleOutput.WriteLine("  builder.Services.AddBirkoSecurity(options =>");
            ExampleOutput.WriteLine("  {");
            ExampleOutput.WriteLine("      options.Jwt.Secret = \"my-secret-key-at-least-32-chars\";");
            ExampleOutput.WriteLine("      options.Jwt.Issuer = \"myapp\";");
            ExampleOutput.WriteLine("      options.Jwt.Audience = \"myapp-api\";");
            ExampleOutput.WriteLine("      options.TenantResolver = TenantResolverType.Header;");
            ExampleOutput.WriteLine("  });\n");

            ExampleOutput.WriteLine("Services registered:");
            ExampleOutput.WriteLine("  JWT Bearer Authentication (AddAuthentication + AddJwtBearer)");
            ExampleOutput.WriteLine("  ICurrentUser          → ClaimsCurrentUser (scoped)");
            ExampleOutput.WriteLine("  IPermissionChecker    → ClaimsPermissionChecker (scoped)");
            ExampleOutput.WriteLine("  ITenantResolver       → HeaderTenantResolver (scoped)");
            ExampleOutput.WriteLine("  ITenantContext         → TenantContextAdapter (scoped)");
            ExampleOutput.WriteLine("  TokenServiceAdapter   → singleton");
            ExampleOutput.WriteLine("  ClaimMappingOptions   → singleton");

            ExampleOutput.WriteLine("\nBirkoSecurityOptions:");
            var opts = new BirkoSecurityOptions();
            ExampleOutput.WriteLine($"  TenantResolver: {opts.TenantResolver} (default)");
            ExampleOutput.WriteLine($"  WildcardPermission: {opts.WildcardPermissionEnabled} (default)");

            ExampleOutput.WriteLine("\nTenant resolver strategies:");
            ExampleOutput.WriteLine("  TenantResolverType.Header    → X-Tenant-Id / X-Tenant-Name headers");
            ExampleOutput.WriteLine("  TenantResolverType.Subdomain → hostname subdomain + async lookup");
            ExampleOutput.WriteLine("  TenantResolverType.Custom    → register ITenantResolver yourself");

            ExampleOutput.WriteLine("\nMiddleware pipeline:");
            ExampleOutput.WriteLine("  app.UseAuthentication();");
            ExampleOutput.WriteLine("  app.UseAuthorization();");
            ExampleOutput.WriteLine("  app.UseTenantMiddleware();  // resolves tenant per request");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        // --- Helpers ---

        private static ICurrentUser CreateCurrentUser(Guid userId, string email,
            string[]? roles = null, string[]? permissions = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Email, email)
            };
            foreach (var role in roles ?? Array.Empty<string>())
                claims.Add(new Claim(ClaimTypes.Role, role));
            foreach (var perm in permissions ?? Array.Empty<string>())
                claims.Add(new Claim(JwtClaimNames.Permission, perm));

            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext { User = principal };
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            return new ClaimsCurrentUser(accessor, new ClaimMappingOptions());
        }

        private static DefaultEndpointFilterInvocationContext CreateEndpointContext(ICurrentUser user)
        {
            var services = new ServiceCollection();
            services.AddSingleton(user);
            var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            return new DefaultEndpointFilterInvocationContext(httpContext);
        }
    }
}
