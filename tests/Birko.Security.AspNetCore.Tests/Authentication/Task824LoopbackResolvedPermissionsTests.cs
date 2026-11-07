using System.Collections.Concurrent;
using System.Net;
using Birko.Security.AspNetCore.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

/// <summary>
/// TASK-824 — the loopback bypass under <c>UseResolvedPermissions</c>. The bypass runs an unauthenticated loopback
/// request as a principal holding <c>"*"</c> and user id <c>Guid.Empty</c>; <see cref="PermissionResolutionMiddleware"/>
/// asked the host's resolver about <c>Guid.Empty</c> and stored its answer, so the bypass's <c>"*"</c> was never read and
/// a bypassed request got whatever the host answers for an empty id (here, as usually, nothing).
/// </summary>
public class Task824LoopbackResolvedPermissionsTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!";

    /// <summary>Answers nothing, as a host's resolver does for an id it does not know, and records what it was asked.</summary>
    private sealed class EmptyResolver : IUserPermissionResolver
    {
        public static readonly ConcurrentQueue<Guid> Asked = new();

        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        {
            Asked.Enqueue(userId);
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        }

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private static async Task<WebApplication> StartAsync(bool wildcardEnabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = new[] { "--urls", "http://127.0.0.1:0" } });
        builder.Logging.ClearProviders();
        builder.Services.AddAuthorization();
        builder.Services.UseResolvedPermissions();
        builder.Services.AddScoped<IUserPermissionResolver, EmptyResolver>();
        builder.Services.AddBirkoSecurity(o =>
        {
            o.Jwt.Secret = Secret;
            o.Jwt.Issuer = "test";
            o.Jwt.Audience = "test";
            o.RequireTenantHeaderMatchesClaim = false;
            o.LoopbackBypass = true;
            o.WildcardPermissionEnabled = wildcardEnabled;
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseBirkoLoopbackBypass();
        app.UseBirkoPermissionResolution();
        app.UseAuthorization();
        app.MapGet("/secure", (HttpContext http, ICurrentUser user) =>
                $"{http.User.Identity?.AuthenticationType}|{string.Join(",", user.Permissions)}")
            .RequireAuthorization()
            .RequirePermission("runs:read");
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(address).Port}") };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // TASK-823, the user's decision: the bypass keeps its "*" when the wildcard option is off
    public async Task A_bypassed_request_gets_the_bypass_wildcard_not_the_resolvers_answer_for_an_empty_id(bool wildcardEnabled)
    {
        await using var app = await StartAsync(wildcardEnabled);
        using var http = Client(app);
        while (EmptyResolver.Asked.TryDequeue(out _)) { }

        var response = await http.GetAsync("/secure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be($"{LoopbackBypass.AuthenticationType}|*");
        EmptyResolver.Asked.Should().NotContain(Guid.Empty, "the bypass principal is not a user the host's resolver knows");
    }
}
