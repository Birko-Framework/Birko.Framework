using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Birko.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

/// <summary>
/// TASK-176 — the token adapter's low findings: SH-L218 (it wrote the ClaimTypes URIs, not the JwtClaimNames it
/// advertises), SH-L236 (a role or permission holding ',' or ';' was split differently by different readers) and SH-L237
/// (no constructor guards; a null Email failed deep in the provider).
/// </summary>
public class Task176TokenAdapterTests
{
    private const string Secret = "task176-token-adapter-tests-secret-key-0123456789";

    private static TokenServiceAdapter Adapter() => new(
        new JwtTokenProvider(new TokenOptions { Secret = Secret, Issuer = "i", Audience = "a", ExpirationMinutes = 5 }),
        new JwtAuthenticationOptions { Secret = Secret, Issuer = "i", Audience = "a" });

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet();

    // ── SH-L218 ──

    [Fact]
    public void The_token_carries_sub_and_email_not_the_ClaimTypes_URIs()
    {
        var user = Guid.NewGuid();
        var token = Adapter().GenerateAccessToken(new TokenRequest(user, "u@example.com")).Token;

        var payload = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList(); // raw, no inbound mapping
        payload.Should().Contain(c => c.Type == JwtClaimNames.UserId && c.Value == user.ToString());
        payload.Should().Contain(c => c.Type == JwtClaimNames.Email && c.Value == "u@example.com");
        payload.Should().NotContain(c => c.Type == ClaimTypes.NameIdentifier || c.Type == ClaimTypes.Email);
    }

    [Fact]
    public void The_adapter_reads_its_own_token_back()
    {
        var user = Guid.NewGuid();
        var adapter = Adapter();

        var info = adapter.ValidateToken(adapter.GenerateAccessToken(new TokenRequest(user, "u@example.com")).Token);

        info.IsValid.Should().BeTrue();
        info.UserGuid.Should().Be(user);
        info.Email.Should().Be("u@example.com");
    }

    [Fact]
    public void The_bearer_handler_maps_sub_and_email_to_what_ClaimMappingOptions_reads()
    {
        var services = new ServiceCollection();
        services.AddBirkoJwtBearer(o => { o.Secret = Secret; o.Issuer = "i"; o.Audience = "a"; });
        using var sp = services.BuildServiceProvider();
        var bearer = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        // The mapping the defaults rest on: "sub" -> NameIdentifier, "email" -> Email.
        bearer.MapInboundClaims.Should().BeTrue();
        new ClaimMappingOptions().UserIdClaim.Should().Be(ClaimTypes.NameIdentifier);
    }

    // ── SH-L236 ──

    [Theory]
    [InlineData("orders:read;orders:delete")]
    [InlineData("orders:read,orders:delete")]
    public void A_permission_holding_a_separator_is_refused(string permission)
    {
        var act = () => Adapter().GenerateAccessToken(new TokenRequest(Guid.NewGuid(), "u@example.com", Permissions: Set("a", permission)));
        act.Should().Throw<ArgumentException>().WithMessage($"*'{permission}'*");
    }

    [Fact]
    public void A_role_holding_a_separator_is_refused()
    {
        var act = () => Adapter().GenerateAccessToken(new TokenRequest(Guid.NewGuid(), "u@example.com", Roles: Set("Admin;Owner")));
        act.Should().Throw<ArgumentException>();
    }

    // ── SH-L237 ──

    [Fact]
    public void The_constructor_refuses_nulls()
    {
        var provider = new JwtTokenProvider(new TokenOptions { Secret = Secret, Issuer = "i", Audience = "a" });
        FluentActions.Invoking(() => new TokenServiceAdapter(null!, new JwtAuthenticationOptions())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new TokenServiceAdapter(provider, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_null_request_or_email_is_refused_naming_it()
    {
        FluentActions.Invoking(() => Adapter().GenerateAccessToken(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Adapter().GenerateAccessToken(new TokenRequest(Guid.NewGuid(), null!)))
            .Should().Throw<ArgumentException>().WithMessage("*Email*");
    }
}
