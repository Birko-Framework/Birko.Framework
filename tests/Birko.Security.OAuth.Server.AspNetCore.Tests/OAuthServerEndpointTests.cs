using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Birko.Security.Jwt;
using Birko.Security.OAuth.Server.AspNetCore;
using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.InMemory.Extensions;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Birko.Security.OAuth.Server.AspNetCore.Tests;

/// <summary>
/// TASK-519: <see cref="OAuthServerEndpointExtensions.MapOAuthServer"/> over a real <see cref="TestServer"/>: each
/// endpoint's success and error shape, form and HTTP Basic parsing, the signed-in user taken from the principal, gated
/// registration, and a grant handed to the app.
/// </summary>
public class OAuthServerEndpointTests
{
    /// <summary>Signs a request in as the user named by the <c>X-Test-User</c> header (roles in <c>X-Test-Roles</c>).</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("sub", user.ToString()) };
            foreach (var role in Request.Headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }

    private static readonly TokenOptions Tokens = new()
    {
        Secret = "test-secret-test-secret-test-secret-test-secret",
        Issuer = "https://auth.test/",
        Audience = "https://api.test/",
        ExpirationMinutes = 60,
    };

    private static async Task<(IHost host, HttpClient http)> StartAsync(Action<OAuthServerEndpointOptions>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", null);
                    services.AddAuthorization(o => o.AddPolicy("admin", p => p.RequireRole("admin")));
                    services.AddOAuthServerInMemoryStores();
                    services.AddSingleton(sp => new OAuthServer(
                        new OAuthServerSettings { Location = "https://auth.test/", Issuer = "https://auth.test/" },
                        new JwtTokenProvider(Tokens),
                        Tokens,
                        sp.GetRequiredService<IOAuthClientStore>(),
                        sp.GetRequiredService<IAuthorizationCodeStore>(),
                        sp.GetRequiredService<IRefreshTokenStore>(),
                        sp.GetRequiredService<IDeviceCodeStore>(),
                        sp.GetRequiredService<IConsentStore>(),
                        "https://auth.test/device"));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapOAuthServer(configure));
                });
            })
            .StartAsync();
        return (host, host.GetTestClient());
    }

    private static async Task AddClientAsync(IHost host, string id, string? secret, params string[] grants)
    {
        await host.Services.GetRequiredService<IOAuthClientStore>().CreateAsync(new OAuthClient
        {
            ClientId = id,
            ClientSecretHash = secret == null ? null : ClientSecretHasher.Hash(secret),
            ClientType = secret == null ? OAuthClientType.Public : OAuthClientType.Confidential,
            Name = id,
            RedirectUris = { "https://app.test/callback" },
            AllowedGrantTypes = grants.ToList(),
            AllowedScopes = { "read", "write" },
            IsEnabled = true,
        });
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static AuthenticationHeaderValue Basic(string id, string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebUtility.UrlEncode(id)}:{WebUtility.UrlEncode(secret)}")));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── /token ──

    [Fact]
    public async Task Token_with_Basic_credentials_answers_snake_case_and_is_not_cached()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "svc", "s3cret", OAuthGrantTypes.ClientCredentials);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/token")
        {
            Content = Form(("grant_type", "client_credentials"), ("scope", "read")),
        };
        request.Headers.Authorization = Basic("svc", "s3cret");
        var response = await http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
        body.GetProperty("scope").GetString().Should().Be("read");
        body.TryGetProperty("refresh_token", out _).Should().BeFalse("nulls are omitted");
        response.Headers.CacheControl!.NoStore.Should().BeTrue("RFC 6749 §5.1");
        response.Headers.Pragma.ToString().Should().Be("no-cache");
    }

    [Fact]
    public async Task Token_with_body_credentials_works_too()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "svc", "s3cret", OAuthGrantTypes.ClientCredentials);

        var response = await http.PostAsync("/token",
            Form(("grant_type", "client_credentials"), ("client_id", "svc"), ("client_secret", "s3cret")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Basic_secret_is_form_decoded()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "svc", "a b+c", OAuthGrantTypes.ClientCredentials);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials")) };
        request.Headers.Authorization = Basic("svc", "a b+c"); // encoded as a+b%2Bc, per RFC 6749 §2.3.1
        (await http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_bad_Basic_secret_is_401_with_a_challenge_and_a_bad_body_secret_400()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "svc", "s3cret", OAuthGrantTypes.ClientCredentials);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials")) };
        request.Headers.Authorization = Basic("svc", "wrong");
        var basic = await http.SendAsync(request);
        basic.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "RFC 6749 §5.2: invalid_client after HTTP Basic");
        basic.Headers.WwwAuthenticate.ToString().Should().StartWith("Basic");
        (await Json(basic)).GetProperty("error").GetString().Should().Be("invalid_client");

        var body = await http.PostAsync("/token",
            Form(("grant_type", "client_credentials"), ("client_id", "svc"), ("client_secret", "wrong")));
        body.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(body)).GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task Malformed_or_doubled_client_authentication_is_refused()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "svc", "s3cret", OAuthGrantTypes.ClientCredentials);

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials"), ("client_id", "svc"), ("client_secret", "s3cret")) };
        malformed.Headers.Authorization = new AuthenticationHeaderValue("Basic", "!!not-base64!!");
        var m = await http.SendAsync(malformed);
        m.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a malformed Basic header is not ignored in favour of the body");

        using var both = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials"), ("client_secret", "s3cret")) };
        both.Headers.Authorization = Basic("svc", "s3cret");
        var b = await http.SendAsync(both);
        b.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(b)).GetProperty("error").GetString().Should().Be("invalid_request");

        using var mismatch = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials"), ("client_id", "other")) };
        mismatch.Headers.Authorization = Basic("svc", "s3cret");
        (await Json(await http.SendAsync(mismatch))).GetProperty("error").GetString().Should().Be("invalid_request");
    }

    [Fact]
    public async Task Token_refuses_a_non_form_body_and_an_unknown_grant()
    {
        var (host, http) = await StartAsync();
        using var owned = host;

        var json = await http.PostAsJsonAsync("/token", new { grant_type = "client_credentials" });
        json.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(json)).GetProperty("error").GetString().Should().Be("invalid_request");

        var unknown = await http.PostAsync("/token", Form(("grant_type", "password"), ("client_id", "x")));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(unknown)).GetProperty("error").GetString().Should().Be("unsupported_grant_type");
    }

    [Fact]
    public async Task A_grant_handler_takes_over_its_grant_type_and_can_add_one()
    {
        var seen = new List<TokenRequest>();
        var (host, http) = await StartAsync(o =>
        {
            o.GrantHandlers[OAuthGrantTypes.ClientCredentials] = (req, _, _) =>
            {
                seen.Add(req);
                return Task.FromResult(new TokenResponse { AccessToken = "app-issued", ExpiresIn = 60, Scope = req.Scope });
            };
            o.GrantHandlers["urn:example:custom"] = (_, _, _) =>
                throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "nope");
        });
        using var owned = host;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = Form(("grant_type", "client_credentials"), ("scope", "read")) };
        request.Headers.Authorization = Basic("svc", "s3cret");
        var response = await http.SendAsync(request);

        (await Json(response)).GetProperty("access_token").GetString().Should().Be("app-issued");
        seen.Should().ContainSingle().Which.Should().Match<TokenRequest>(r => r.ClientId == "svc" && r.ClientSecret == "s3cret" && r.Scope == "read");

        var custom = await http.PostAsync("/token", Form(("grant_type", "urn:example:custom")));
        (await Json(custom)).GetProperty("error").GetString().Should().Be("invalid_grant", "a handler's protocol error is mapped like the server's");
    }

    // ── device flow ──

    [Fact]
    public async Task Device_flow_end_to_end_with_the_approver_taken_from_the_principal()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "cli", null, OAuthGrantTypes.DeviceCode);

        var start = await http.PostAsync("/device_authorization", Form(("client_id", "cli"), ("scope", "read")));
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        start.Headers.CacheControl!.NoStore.Should().BeTrue();
        var device = await Json(start);
        var userCode = device.GetProperty("user_code").GetString()!;
        device.GetProperty("verification_uri").GetString().Should().Be("https://auth.test/device");

        (await http.PostAsJsonAsync("/device/approve", new { user_code = userCode, approved = true }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "approval acts for a signed-in user");

        using var approve = new HttpRequestMessage(HttpMethod.Post, "/device/approve")
        {
            Content = JsonContent.Create(new { user_code = userCode, approved = true, user_id = "spoofed" }),
        };
        approve.Headers.Add("X-Test-User", "alice");
        (await http.SendAsync(approve)).StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await http.PostAsync("/token", Form(
            ("grant_type", OAuthGrantTypes.DeviceCode), ("client_id", "cli"), ("device_code", device.GetProperty("device_code").GetString()!)));
        token.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken((await Json(token)).GetProperty("access_token").GetString());
        jwt.Subject.Should().Be("alice", "the approver is the principal, never a body field");
    }

    [Fact]
    public async Task Device_errors_have_the_RFC_shape()
    {
        var (host, http) = await StartAsync();
        using var owned = host;

        var unknown = await http.PostAsync("/device_authorization", Form(("client_id", "nobody")));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(unknown)).TryGetProperty("error", out _).Should().BeTrue();

        using var noCode = new HttpRequestMessage(HttpMethod.Post, "/device/approve") { Content = JsonContent.Create(new { approved = true }) };
        noCode.Headers.Add("X-Test-User", "alice");
        var missing = await http.SendAsync(noCode);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(missing)).GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── /authorize ──

    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private static string AuthorizeUrl(string redirect = "https://app.test/callback") =>
        $"/authorize?response_type=code&client_id=web&redirect_uri={Uri.EscapeDataString(redirect)}&scope=read&state=xyz&code_challenge={Challenge}&code_challenge_method=S256";

    private static async Task ConsentAsync(IHost host, string user) =>
        await host.Services.GetRequiredService<IConsentStore>().CreateAsync(new ConsentRecord { UserId = user, ClientId = "web", Scope = "read" });

    [Fact]
    public async Task Authorize_needs_a_signed_in_user_and_reports_consent_or_a_code()
    {
        var (host, http) = await StartAsync();
        using var owned = host;
        await AddClientAsync(host, "web", null, OAuthGrantTypes.AuthorizationCode);

        (await http.GetAsync(AuthorizeUrl())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var first = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl());
        first.Headers.Add("X-Test-User", "alice");
        var consent = await Json(await http.SendAsync(first));
        consent.GetProperty("requires_consent").GetBoolean().Should().BeTrue();

        await ConsentAsync(host, "alice");
        using var second = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl());
        second.Headers.Add("X-Test-User", "alice");
        var issued = await Json(await http.SendAsync(second));
        issued.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        issued.GetProperty("state").GetString().Should().Be("xyz");

        var token = await http.PostAsync("/token", Form(("grant_type", "authorization_code"), ("client_id", "web"),
            ("code", issued.GetProperty("code").GetString()!), ("redirect_uri", "https://app.test/callback"), ("code_verifier", Verifier)));
        token.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Redirect_mode_sends_the_code_to_the_registered_uri_and_never_redirects_an_error()
    {
        var (host, http) = await StartAsync(o => o.AuthorizeResponseMode = OAuthAuthorizeResponseMode.Redirect);
        using var owned = host;
        await AddClientAsync(host, "web", null, OAuthGrantTypes.AuthorizationCode);
        await ConsentAsync(host, "alice");

        using var ok = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl());
        ok.Headers.Add("X-Test-User", "alice");
        var redirect = await http.SendAsync(ok);
        redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = redirect.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be("https://app.test/callback");
        location.Query.Should().Contain("code=").And.Contain("state=xyz");

        using var bad = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl("https://evil.test/steal"));
        bad.Headers.Add("X-Test-User", "alice");
        var refused = await http.SendAsync(bad);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unregistered redirect_uri is never redirected to");
        refused.Headers.Location.Should().BeNull();
    }

    // ── registration ──

    [Fact]
    public async Task Registration_is_off_by_default()
    {
        var (host, http) = await StartAsync();
        using var owned = host;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/register") { Content = JsonContent.Create(new { name = "x" }) };
        request.Headers.Add("X-Test-User", "admin");
        (await http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Registration_when_on_is_gated_by_the_apps_policy_and_disable_works()
    {
        var (host, http) = await StartAsync(o =>
        {
            o.AllowDynamicRegistration = true;
            o.ConfigureRegistration = b => b.RequireAuthorization("admin");
        });
        using var owned = host;
        var registration = new { name = "app", clientType = "Confidential", allowedGrantTypes = new[] { "client_credentials" }, allowedScopes = new[] { "read" } };

        (await http.PostAsJsonAsync("/register", registration)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var notAdmin = new HttpRequestMessage(HttpMethod.Post, "/register") { Content = JsonContent.Create(registration) };
        notAdmin.Headers.Add("X-Test-User", "bob");
        (await http.SendAsync(notAdmin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = new HttpRequestMessage(HttpMethod.Post, "/register") { Content = JsonContent.Create(registration) };
        admin.Headers.Add("X-Test-User", "root");
        admin.Headers.Add("X-Test-Roles", "admin");
        var created = await http.SendAsync(admin);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.CacheControl!.NoStore.Should().BeTrue("the response carries the client secret");
        var client = await Json(created);
        var clientId = client.GetProperty("client_id").GetString()!;
        var secret = client.GetProperty("client_secret").GetString()!;

        using var disable = new HttpRequestMessage(HttpMethod.Post, $"/register/{clientId}/disable");
        disable.Headers.Add("X-Test-User", "root");
        disable.Headers.Add("X-Test-Roles", "admin");
        (await http.SendAsync(disable)).StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await http.PostAsync("/token", Form(("grant_type", "client_credentials"), ("client_id", clientId), ("client_secret", secret)));
        (await Json(token)).GetProperty("error").GetString().Should().Be("invalid_client", "a disabled client is refused");

        using var numeric = new HttpRequestMessage(HttpMethod.Post, "/register")
        {
            Content = JsonContent.Create(new { name = "app2", clientType = 0, allowedGrantTypes = new[] { "device_code" } }),
        };
        numeric.Headers.Add("X-Test-User", "root");
        numeric.Headers.Add("X-Test-Roles", "admin");
        var publicClient = await Json(await http.SendAsync(numeric));
        publicClient.GetProperty("client_type").GetString().Should().Be("Public", "the numeric form is still accepted");

        using var missing = new HttpRequestMessage(HttpMethod.Post, "/register/nobody/disable");
        missing.Headers.Add("X-Test-User", "root");
        missing.Headers.Add("X-Test-Roles", "admin");
        (await http.SendAsync(missing)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
