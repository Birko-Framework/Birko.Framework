using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

/// <summary>TASK-778: Google's two issuers, Microsoft's <c>{tenantid}</c> issuer and the allowed tenants.</summary>
public class IssuerAndTenantTests
{
    private const string TenantA = "6f1c2a3b-4d5e-4f60-8a1b-2c3d4e5f6a7b";
    private const string TenantB = "11111111-2222-4333-8444-555555555555";
    private const string MicrosoftTemplate = "https://login.microsoftonline.com/{tenantid}/v2.0";

    private static string MicrosoftIssuer(string tid) => MicrosoftTemplate.Replace("{tenantid}", tid);

    [Fact]
    public async Task Google_issues_with_or_without_the_scheme()
    {
        var h = new SignInHarness(o => o.AdditionalIssuers.Add("accounts.google.com"));
        h.Provider.DiscoveryIssuer = "https://accounts.google.com";

        (await h.SignInWithClaimsAsync(c => c["iss"] = "https://accounts.google.com")).Status.Should().Be(OpenIdSignInStatus.Succeeded);
        (await h.SignInWithClaimsAsync(c => c["iss"] = "accounts.google.com")).Status.Should().Be(OpenIdSignInStatus.Succeeded);
        (await h.SignInWithClaimsAsync(c => c["iss"] = "https://accounts.google.com.evil.test")).Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
    }

    [Fact]
    public async Task Microsofts_template_issuer_takes_the_tokens_own_tid()
    {
        var h = new SignInHarness();
        h.Provider.DiscoveryIssuer = MicrosoftTemplate;

        var ok = await h.SignInWithClaimsAsync(c => { c["iss"] = MicrosoftIssuer(TenantA); c["tid"] = TenantA; });

        ok.Status.Should().Be(OpenIdSignInStatus.Succeeded, ok.Error);
        ok.Identity!.TenantId.Should().Be(TenantA);
        ok.Identity.Issuer.Should().Be(MicrosoftIssuer(TenantA));
    }

    [Fact]
    public async Task Microsofts_issuer_of_another_tenant_than_tid_is_refused()
    {
        var h = new SignInHarness();
        h.Provider.DiscoveryIssuer = MicrosoftTemplate;

        (await h.SignInWithClaimsAsync(c => { c["iss"] = MicrosoftIssuer(TenantB); c["tid"] = TenantA; }))
            .Error.Should().Be("wrong issuer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("evil.test/x")]
    public async Task Microsofts_template_needs_a_tid_that_is_a_tenant_id(string? tid)
    {
        var h = new SignInHarness();
        h.Provider.DiscoveryIssuer = MicrosoftTemplate;

        var result = await h.SignInWithClaimsAsync(c =>
        {
            c["iss"] = MicrosoftIssuer(tid ?? "");
            if (tid != null) c["tid"] = tid;
        });

        result.Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
        result.Error.Should().Be("tid is not a tenant id");
    }

    [Fact]
    public async Task The_template_never_matches_a_token_issuer_with_the_placeholder_left_in()
    {
        var h = new SignInHarness();
        h.Provider.DiscoveryIssuer = MicrosoftTemplate;

        (await h.SignInWithClaimsAsync(c => { c["iss"] = MicrosoftTemplate; c["tid"] = TenantA; }))
            .Status.Should().Be(OpenIdSignInStatus.InvalidIdToken);
    }

    [Fact]
    public async Task Allowed_tenants_refuse_any_other_tenant()
    {
        var h = new SignInHarness(o => o.AllowedTenantIds.Add(Guid.Parse(TenantA)));
        h.Provider.DiscoveryIssuer = MicrosoftTemplate;

        (await h.SignInWithClaimsAsync(c => { c["iss"] = MicrosoftIssuer(TenantA); c["tid"] = TenantA; }))
            .Status.Should().Be(OpenIdSignInStatus.Succeeded);
        (await h.SignInWithClaimsAsync(c => { c["iss"] = MicrosoftIssuer(TenantB); c["tid"] = TenantB; }))
            .Error.Should().Be("tenant not allowed");
    }

    [Fact]
    public async Task Allowed_tenants_refuse_a_token_without_tid()
    {
        var h = new SignInHarness(o => o.AllowedTenantIds.Add(Guid.Parse(TenantA)));
        (await h.SignInWithClaimsAsync(_ => { })).Error.Should().Be("tenant not allowed");
    }
}
