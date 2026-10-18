using System.Net;
using System.Net.Http.Headers;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

/// <summary>
/// TASK-522: <see cref="BirkoSecurityOptions.LoopbackBypass"/>. The decision (off never bypasses; all-loopback
/// bypasses; any non-loopback, wildcard or unknown address enforces) and the middleware on a real Kestrel host, where
/// the addresses the server actually bound decide, not the configured URLs.
/// </summary>
public class LoopbackBypassTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!";

    // ── the decision ──

    [Fact]
    public void Off_never_bypasses() =>
        LoopbackBypass.ShouldBypass(false, new[] { "http://127.0.0.1:5000" }).Should().BeFalse();

    [Theory]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://[::1]:5000")]
    [InlineData("https://127.0.0.2:443")]
    [InlineData("127.0.0.1:8080")]
    public void All_loopback_bypasses(string address) =>
        LoopbackBypass.ShouldBypass(true, new[] { address, "http://localhost:6000" }).Should().BeTrue();

    [Theory]
    [InlineData("http://0.0.0.0:5000")]
    [InlineData("http://[::]:5000")]
    [InlineData("http://*:5000")]
    [InlineData("http://+:5000")]
    [InlineData("http://192.168.1.10:5000")]
    [InlineData("http://example.com:5000")]
    [InlineData("not a url ::")]
    public void Any_non_loopback_address_enforces(string address) =>
        LoopbackBypass.ShouldBypass(true, new[] { "http://127.0.0.1:5000", address }).Should().BeFalse();

    [Fact]
    public void Unknown_addresses_enforce()
    {
        LoopbackBypass.ShouldBypass(true, null).Should().BeFalse();
        LoopbackBypass.ShouldBypass(true, Array.Empty<string>()).Should().BeFalse();
        LoopbackBypass.ShouldBypass(true, new[] { " ", "" }).Should().BeFalse();
    }

    // ── on a real Kestrel host ──

    private static async Task<WebApplication> StartAsync(bool bypass, bool listenAnyIp = false, bool wireMiddleware = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = new[] { "--urls", "http://127.0.0.1:0" } });
        builder.Logging.ClearProviders();
        if (listenAnyIp)
            builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(0)); // overrides the configured loopback URL
        builder.Services.AddAuthorization();
        builder.Services.AddBirkoSecurity(o =>
        {
            o.Jwt.Secret = Secret;
            o.Jwt.Issuer = "test";
            o.Jwt.Audience = "test";
            o.RequireTenantHeaderMatchesClaim = false;
            o.LoopbackBypass = bypass;
        });

        var app = builder.Build();
        app.UseAuthentication();
        if (wireMiddleware) app.UseBirkoLoopbackBypass();
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
        var port = new Uri(address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost")).Port;
        return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    [Fact]
    public async Task A_loopback_only_server_lets_a_request_without_a_token_through_with_the_wildcard_permission()
    {
        await using var app = await StartAsync(bypass: true);
        using var http = Client(app);

        var response = await http.GetAsync("/secure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be($"{LoopbackBypass.AuthenticationType}|*");
    }

    [Fact]
    public async Task The_bound_addresses_decide_not_the_configured_urls()
    {
        // Configured --urls http://127.0.0.1:0, but ListenAnyIP binds every interface. DraCode's rule read the
        // configured URL and bypassed (measured); the bound address is [::], so this must enforce.
        await using var app = await StartAsync(bypass: true, listenAnyIp: true);
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
            .Should().OnlyContain(a => !a.Contains("127.0.0.1"));
        using var http = Client(app);

        (await http.GetAsync("/secure")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Off_by_default_a_loopback_server_still_requires_a_token()
    {
        await using var app = await StartAsync(bypass: false);
        using var http = Client(app);

        (await http.GetAsync("/secure")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_request_with_a_valid_token_keeps_its_own_principal()
    {
        await using var app = await StartAsync(bypass: true);
        using var http = Client(app);
        var token = new JwtTokenProvider(new TokenOptions { Secret = Secret, Issuer = "test", Audience = "test", ExpirationMinutes = 5 })
            .GenerateToken(new Dictionary<string, string> { ["sub"] = Guid.NewGuid().ToString(), ["permission"] = "runs:read" });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var body = await (await http.SendAsync(request)).Content.ReadAsStringAsync();

        body.Should().NotStartWith(LoopbackBypass.AuthenticationType).And.EndWith("|runs:read");
    }

    [Fact]
    public async Task Turned_on_without_its_middleware_the_host_refuses_to_start()
    {
        var start = () => StartAsync(bypass: true, wireMiddleware: false);

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseBirkoLoopbackBypass*");
    }
}
