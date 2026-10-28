using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

/// <summary>TASK-778: the Google and Microsoft factories, and the options a sign-in cannot work with.</summary>
public class ProviderOptionsTests
{
    [Fact]
    public void Google_reads_its_discovery_document_and_accepts_both_issuers()
    {
        var o = GoogleOpenIdProvider.Create("id", "secret", "https://app.test/cb");

        o.Name.Should().Be("google");
        o.MetadataAddress.Should().Be("https://accounts.google.com/.well-known/openid-configuration");
        o.AdditionalIssuers.Should().Equal("accounts.google.com");
        o.Scope.Should().Be("openid email profile");
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    [InlineData("6f1c2a3b-4d5e-4f60-8a1b-2c3d4e5f6a7b")]
    public void Microsoft_takes_its_tenants(string tenant)
    {
        var o = MicrosoftOpenIdProvider.Create("id", "secret", "https://app.test/cb", tenant);

        o.Name.Should().Be("microsoft");
        o.MetadataAddress.Should().Be($"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration");
    }

    [Theory]
    [InlineData("contoso.com")]
    [InlineData("common/../evil")]
    [InlineData("")]
    public void Microsoft_refuses_any_other_tenant(string tenant) =>
        FluentActions.Invoking(() => MicrosoftOpenIdProvider.Create("id", "secret", "https://app.test/cb", tenant))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Microsoft_carries_the_allowed_tenants()
    {
        var tenant = Guid.NewGuid();
        MicrosoftOpenIdProvider.Create("id", "secret", "https://app.test/cb", allowedTenantIds: new[] { tenant })
            .AllowedTenantIds.Should().Equal(tenant);
    }

    [Fact]
    public void Every_reserved_authorization_parameter_is_refused()
    {
        foreach (var name in OpenIdProviderOptions.ReservedAuthorizationParameters)
        {
            var o = new OpenIdProviderOptions
            {
                Name = "x", MetadataAddress = "https://idp.test/.well-known/openid-configuration", ClientId = "id", RedirectUri = "https://app.test/cb",
            };
            o.AuthorizationParameters[name] = "x";
            FluentActions.Invoking(o.Validate).Should().Throw<ArgumentException>().WithMessage($"*'{name}'*");
        }
    }

    [Fact]
    public async Task A_sign_in_can_share_the_apps_validator()
    {
        var h = new SignInHarness();
        var http = new HttpClient(h.Provider);
        var validator = new IdTokenValidator(h.Options, http, h.Clock);
        var signIn = new OpenIdSignIn(validator, h.States, http);

        var start = await signIn.BeginAsync(SignInHarness.Binding);
        h.Provider.NextIdToken = h.Provider.Sign(h.Claims(SignInHarness.Query(new Uri(start.AuthorizationUrl))["nonce"]));
        var result = await signIn.CompleteAsync(new OpenIdCallback(start.State, "good"), SignInHarness.Binding);

        result.Succeeded.Should().BeTrue(result.Error);
        signIn.Provider.Should().Be("test");
        (await validator.ValidateAsync(h.Provider.Sign(h.Claims("n")), "n")).IsValid.Should().BeTrue();
        h.Provider.DiscoveryRequests.Should().Be(1, "the sign-in and the app use one key cache");
    }

    [Theory]
    [InlineData("scope", "email profile")]
    [InlineData("metadata", "http://idp.test/.well-known/openid-configuration")]
    [InlineData("redirect", "/relative")]
    [InlineData("client", "")]
    [InlineData("nonce-param", "")]
    public void Options_that_cannot_work_are_refused(string what, string value)
    {
        var o = new OpenIdProviderOptions
        {
            Name = "x", MetadataAddress = "https://idp.test/.well-known/openid-configuration", ClientId = "id", RedirectUri = "https://app.test/cb",
        };
        switch (what)
        {
            case "scope": o.Scope = value; break;
            case "metadata": o.MetadataAddress = value; break;
            case "redirect": o.RedirectUri = value; break;
            case "client": o.ClientId = value; break;
            case "nonce-param": o.AuthorizationParameters["nonce"] = "fixed"; break;
        }
        FluentActions.Invoking(o.Validate).Should().Throw<ArgumentException>();
    }
}
