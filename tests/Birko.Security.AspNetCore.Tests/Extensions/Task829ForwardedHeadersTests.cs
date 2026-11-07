using System.Net;
using Birko.Communication.WebSocket.Middleware;
using Birko.Communication.WebSocket.Services;
using Birko.Security.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Extensions;

/// <summary>
/// TASK-829: <c>AddBirkoForwardedHeaders</c>, one list of trusted proxies for the whole host, on a real Kestrel host.
/// The test client connects from 127.0.0.1, so "the proxy" here is the test itself.
/// </summary>
public class Task829ForwardedHeadersTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!";

    private sealed record Host(
        Action<IServiceCollection>? Services = null,
        Action<WebApplication>? Pipeline = null,
        string[]? WebSocketProxies = null);

    private static async Task<WebApplication> StartAsync(Host host)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = new[] { "--urls", "http://127.0.0.1:0" } });
        builder.Logging.ClearProviders();
        host.Services?.Invoke(builder.Services);
        builder.Services.Configure<WebSocketAuthenticationConfiguration>(c => c.TrustedProxies = new(host.WebSocketProxies ?? Array.Empty<string>()));
        builder.Services.AddSingleton<WebSocketAuthenticationService>();

        var app = builder.Build();
        host.Pipeline?.Invoke(app);
        app.MapGet("/who", (HttpContext http, WebSocketAuthenticationService ws) =>
            $"{http.Connection.RemoteIpAddress}|{http.Request.Scheme}|{http.Request.Host.Host}|{ws.GetClientIpAddress(http)}");
        await app.StartAsync();
        return app;
    }

    private static async Task<string[]> WhoAsync(WebApplication app, params (string Name, string Value)[] headers)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(address).Port}") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/who");
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        var response = await http.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadAsStringAsync()).Split('|');
    }

    // ── one client address ──

    [Fact]
    public async Task Through_a_trusted_proxy_the_connection_and_the_token_binding_see_one_client()
    {
        await using var app = await StartAsync(new Host(
            s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1")),
            WebSocketProxies: new[] { "127.0.0.1" }));

        var who = await WhoAsync(app, ("X-Forwarded-For", "198.51.100.1, 203.0.113.7"), ("X-Forwarded-Proto", "https"));

        who[0].Should().Be("203.0.113.7");
        who[1].Should().Be("https");
        who[3].Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task The_token_binding_does_not_read_the_headers_a_second_time()
    {
        // The client is inside the trusted network. ASP.NET consumes its X-Forwarded-For hop and stops at the forward
        // limit; Birko's own walk, run again on what is left, fell back to X-Real-IP, which ASP.NET never reads and the
        // client wrote itself.
        await using var app = await StartAsync(new Host(
            s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.0/8")),
            WebSocketProxies: new[] { "127.0.0.0/8" }));

        var who = await WhoAsync(app, ("X-Forwarded-For", "127.0.0.5"), ("X-Real-IP", "1.2.3.4"));

        who[0].Should().Be("127.0.0.5");
        who[3].Should().Be("127.0.0.5");
    }

    [Fact]
    public async Task Without_the_registration_the_token_binding_walks_as_before()
    {
        await using var app = await StartAsync(new Host(WebSocketProxies: new[] { "127.0.0.1" }));

        var who = await WhoAsync(app, ("X-Forwarded-For", "203.0.113.7"));

        who[0].Should().Be("127.0.0.1");
        who[3].Should().Be("203.0.113.7");
    }

    // ── nothing configured trusts nobody ──

    [Fact]
    public async Task Nothing_configured_ignores_a_spoofed_header_even_where_something_else_switched_the_headers_on()
    {
        // As ASPNETCORE_FORWARDEDHEADERS_ENABLED or a consumer's own setup does. ASP.NET's defaults trust loopback, so
        // without the registration the header is believed.
        Action<IServiceCollection> switchedOn = s => s.Configure<ForwardedHeadersOptions>(o =>
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        await using var bare = await StartAsync(new Host(switchedOn, a => a.UseForwardedHeaders()));
        (await WhoAsync(bare, ("X-Forwarded-For", "1.2.3.4")))[0].Should().Be("1.2.3.4");

        // Registered before or after the other setup, the registration wins.
        foreach (var registrationFirst in new[] { true, false })
        {
            Action<IServiceCollection> register = s => s.AddBirkoForwardedHeaders(_ => { });
            await using var app = await StartAsync(new Host(
                registrationFirst ? register + switchedOn : switchedOn + register,
                a => a.UseForwardedHeaders()));

            var who = await WhoAsync(app, ("X-Forwarded-For", "1.2.3.4"), ("X-Forwarded-Proto", "https"));

            who[0].Should().Be("127.0.0.1", $"registration first: {registrationFirst}");
            who[1].Should().Be("http");
            who[3].Should().Be("127.0.0.1");
        }
    }

    [Fact]
    public async Task A_second_forwarded_headers_pass_cannot_read_a_hop_the_client_wrote()
    {
        // Limit 1: the first pass takes 127.0.0.9 and stops. A second ForwardedHeadersMiddleware (the app's own, or the
        // one ASPNETCORE_FORWARDEDHEADERS_ENABLED adds) found 127.0.0.9 trusted and believed 1.2.3.4, written by the
        // client. The registration leaves it nothing to read.
        await using var app = await StartAsync(new Host(
            s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.0/8")),
            a => a.UseForwardedHeaders()));

        var who = await WhoAsync(app, ("X-Forwarded-For", "1.2.3.4, 127.0.0.9"));

        who[0].Should().Be("127.0.0.9");
        who[3].Should().Be("127.0.0.9");
    }

    [Fact]
    public async Task An_untrusted_peer_is_not_believed()
    {
        await using var app = await StartAsync(new Host(s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("10.0.0.5"))));

        (await WhoAsync(app, ("X-Forwarded-For", "1.2.3.4")))[0].Should().Be("127.0.0.1");
    }

    // ── host forwarding ──

    [Fact]
    public async Task The_forwarded_host_is_ignored_unless_listed()
    {
        await using var noHosts = await StartAsync(new Host(s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1"))));
        (await WhoAsync(noHosts, ("X-Forwarded-Host", "app.example.com")))[2].Should().Be("127.0.0.1");

        await using var listed = await StartAsync(new Host(s => s.AddBirkoForwardedHeaders(o =>
        {
            o.TrustedProxies.Add("127.0.0.1");
            o.AllowedHosts.Add("app.example.com");
        })));
        (await WhoAsync(listed, ("X-Forwarded-Host", "app.example.com")))[2].Should().Be("app.example.com");
        (await WhoAsync(listed, ("X-Forwarded-Host", "evil.example.com")))[2].Should().Be("127.0.0.1");
    }

    // ── order ──

    [Fact]
    public async Task The_headers_are_applied_before_the_apps_first_middleware()
    {
        string? seen = null;
        await using var app = await StartAsync(new Host(
            s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1")),
            a => a.Use((context, next) =>
            {
                seen = context.Connection.RemoteIpAddress?.ToString();
                return next(context);
            })));

        await WhoAsync(app, ("X-Forwarded-For", "203.0.113.7"));

        seen.Should().Be("203.0.113.7");
    }

    // ── configuration ──

    [Fact]
    public async Task The_configuration_section_binds_the_list_and_the_limit()
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Birko:ForwardedHeaders:TrustedProxies:0"] = "127.0.0.1",
            ["Birko:ForwardedHeaders:TrustedProxies:1"] = "198.51.100.0/24",
            ["Birko:ForwardedHeaders:ForwardLimit"] = "2",
        }).Build().GetSection(BirkoForwardedHeadersOptions.SectionName);

        await using var app = await StartAsync(new Host(s => s.AddBirkoForwardedHeaders(section)));

        // Two trusted hops in a row (Cloudflare, then the local proxy) need the limit of 2.
        (await WhoAsync(app, ("X-Forwarded-For", "203.0.113.7, 198.51.100.9")))[0].Should().Be("203.0.113.7");
        app.Services.GetRequiredService<TrustedProxySettings>().TrustedProxies.Should().Equal("127.0.0.1", "198.51.100.0/24");
    }

    // ── refusals ──

    [Theory]
    [InlineData("10.0.0.300")]
    [InlineData("10.0.0.0/40")]
    [InlineData("unknown")]
    public void A_malformed_entry_refuses(string entry)
    {
        var add = () => new ServiceCollection().AddBirkoForwardedHeaders(o => o.TrustedProxies.Add(entry));

        add.Should().Throw<InvalidOperationException>().WithMessage($"*TrustedProxies*'{entry}'*").WithInnerException<FormatException>();
    }

    [Fact]
    public void An_unset_variable_refuses()
    {
        var entry = "${BIRKO_T829_UNSET_" + Guid.NewGuid().ToString("N") + "}";

        var add = () => new ServiceCollection().AddBirkoForwardedHeaders(o => o.TrustedProxies.Add(entry));

        add.Should().Throw<InvalidOperationException>().WithMessage("*not set*");
    }

    [Fact]
    public void A_limit_below_one_refuses()
    {
        var add = () => new ServiceCollection().AddBirkoForwardedHeaders(o =>
        {
            o.TrustedProxies.Add("10.0.0.5");
            o.ForwardLimit = 0;
        });

        add.Should().Throw<InvalidOperationException>().WithMessage("*ForwardLimit*");
    }

    [Fact]
    public void Allowed_hosts_without_a_proxy_refuse()
    {
        var add = () => new ServiceCollection().AddBirkoForwardedHeaders(o => o.AllowedHosts.Add("app.example.com"));

        add.Should().Throw<InvalidOperationException>().WithMessage("*AllowedHosts*TrustedProxies is empty*");
    }

    [Fact]
    public void Registering_twice_refuses()
    {
        var services = new ServiceCollection().AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("10.0.0.5"));

        var again = () => services.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("10.0.0.6"));

        again.Should().Throw<InvalidOperationException>().WithMessage("*twice*");
    }

    [Fact]
    public async Task A_websocket_list_that_differs_refuses_and_an_empty_one_defers()
    {
        // Refused when the endpoint is mapped, i.e. at startup, before any request builds the service.
        var start = () => StartAsync(new Host(
            s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1")),
            a => a.MapWebSocketEndpoint("/ws", (_, _) => Task.CompletedTask),
            WebSocketProxies: new[] { "10.0.0.5" }));
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*WebSocketAuthenticationConfiguration.TrustedProxies*");

        await using var deferring = await StartAsync(new Host(s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1"))));
        (await WhoAsync(deferring, ("X-Forwarded-For", "203.0.113.7")))[3].Should().Be("203.0.113.7");
    }

    // ── the loopback bypass ──

    private static Action<IServiceCollection> Security(bool bypass) => s =>
    {
        s.AddAuthorization();
        s.AddBirkoSecurity(o =>
        {
            o.Jwt.Secret = Secret;
            o.Jwt.Issuer = "test";
            o.Jwt.Audience = "test";
            o.RequireTenantHeaderMatchesClaim = false;
            o.LoopbackBypass = bypass;
        });
    };

    private static void SecurityPipeline(WebApplication app)
    {
        app.UseAuthentication();
        app.UseBirkoLoopbackBypass();
        app.UseAuthorization();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_loopback_bypass_refuses_to_start_behind_a_trusted_proxy_in_either_registration_order(bool securityFirst)
    {
        Action<IServiceCollection> proxies = s => s.AddBirkoForwardedHeaders(o => o.TrustedProxies.Add("127.0.0.1"));
        var services = securityFirst ? Security(true) + proxies : proxies + Security(true);

        var start = () => StartAsync(new Host(services, SecurityPipeline));

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*LoopbackBypass*TrustedProxies*127.0.0.1*");
    }

    [Fact]
    public async Task The_loopback_bypass_still_starts_with_an_empty_proxy_list()
    {
        Action<IServiceCollection> services = Security(true) + (s => s.AddBirkoForwardedHeaders(_ => { }));

        await using var app = await StartAsync(new Host(services, SecurityPipeline));
    }
}
