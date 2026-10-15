using System.Net.WebSockets;
using Birko.Security.AspNetCore.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

/// <summary>
/// TASK-165: SH-M256 (<c>?token=</c> was accepted on every request) and SH-M255 (<c>UseResolvedPermissions</c> lost to a
/// later <c>AddBirkoSecurity</c>).
/// </summary>
public class Task165QueryTokenAndCurrentUserTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!";

    private sealed class WebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }

    private static async Task<string?> TokenReadFrom(HttpContext http, Action<JwtAuthenticationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBirkoJwtBearer(o =>
        {
            o.Secret = Secret;
            configure?.Invoke(o);
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var context = new MessageReceivedContext(http,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), options);

        await options.Events.MessageReceived(context);
        return context.Token;
    }

    private static DefaultHttpContext Request(string path, string? accept = null, string? authorization = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.QueryString = new QueryString("?token=from-the-url");
        if (accept != null) http.Request.Headers.Accept = accept;
        if (authorization != null) http.Request.Headers.Authorization = authorization;
        return http;
    }

    // ── SH-M256 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_ordinary_request_does_not_take_its_token_from_the_query()
    {
        (await TokenReadFrom(Request("/api/orders"))).Should().BeNull();
    }

    [Fact]
    public async Task An_event_stream_request_takes_its_token_from_the_query()
    {
        (await TokenReadFrom(Request("/api/events", accept: "text/event-stream"))).Should().Be("from-the-url");
    }

    [Fact]
    public async Task A_websocket_upgrade_takes_its_token_from_the_query()
    {
        var http = Request("/ws");
        http.Features.Set<IHttpWebSocketFeature>(new WebSocketFeature());

        (await TokenReadFrom(http)).Should().Be("from-the-url");
    }

    [Theory]
    [InlineData("/api/media/42", true)]
    [InlineData("/API/Media", true)]
    [InlineData("/api/mediaX/42", false)]
    [InlineData("/api/orders", false)]
    public async Task A_configured_path_prefix_takes_its_token_from_the_query(string path, bool accepted)
    {
        var token = await TokenReadFrom(Request(path), o => o.QueryTokenPathPrefixes.Add("/api/media"));

        if (accepted) token.Should().Be("from-the-url");
        else token.Should().BeNull();
    }

    [Fact]
    public async Task An_authorization_header_wins_over_the_query()
    {
        (await TokenReadFrom(Request("/api/events", accept: "text/event-stream", authorization: "Bearer from-the-header")))
            .Should().BeNull("the handler then reads the header itself");
    }

    [Fact]
    public void AddBirkoSecurity_passes_the_prefixes_on()
    {
        var services = new ServiceCollection();
        services.AddBirkoSecurity(o =>
        {
            o.Jwt.Secret = Secret;
            o.Jwt.QueryTokenPathPrefixes.Add("/api/media");
        });

        services.Select(d => d.ImplementationInstance).OfType<JwtAuthenticationOptions>().Single()
            .QueryTokenPathPrefixes.Should().Equal("/api/media");
    }

    // ── SH-M255 ──────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<Type?> CurrentUserRegistrations(IServiceCollection services)
        => services.Where(d => d.ServiceType == typeof(ICurrentUser)).Select(d => d.ImplementationType);

    [Fact]
    public void UseResolvedPermissions_before_AddBirkoSecurity_still_wins()
    {
        var services = new ServiceCollection();
        services.UseResolvedPermissions();
        services.AddBirkoSecurity(o => o.Jwt.Secret = Secret);

        CurrentUserRegistrations(services).Should().Equal(typeof(ResolvedPermissionsCurrentUser));
    }

    [Fact]
    public void UseResolvedPermissions_after_AddBirkoSecurity_wins()
    {
        var services = new ServiceCollection();
        services.AddBirkoSecurity(o => o.Jwt.Secret = Secret);
        services.UseResolvedPermissions();

        CurrentUserRegistrations(services).Should().Equal(typeof(ResolvedPermissionsCurrentUser));
    }

    [Fact]
    public void UseResolvedPermissions_removes_every_earlier_registration()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
        services.UseResolvedPermissions();

        CurrentUserRegistrations(services).Should().Equal(typeof(ResolvedPermissionsCurrentUser));
    }

    [Fact]
    public void AddBirkoSecurity_alone_registers_the_claims_reader()
    {
        var services = new ServiceCollection();
        services.AddBirkoSecurity(o => o.Jwt.Secret = Secret);

        CurrentUserRegistrations(services).Should().Equal(typeof(ClaimsCurrentUser));
    }
}
