using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Birko.Security.Vault.Configuration;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Vault.Tests;

/// <summary>
/// CR-M242: the LocalVault path-building / option-resolution logic (BuildPaths override-ordering and
/// ResolveOptions env-fallback + lower-casing) was untested. Both are private statics on the public
/// extension class, reachable by reflection since the projitems compiles into this assembly.
/// (The SecretConfigurationProvider key-rewriting side is covered by SecretConfigurationProviderTests.)
/// </summary>
public class LocalVaultConfigurationTests
{
    private static readonly MethodInfo BuildPathsMethod =
        typeof(LocalVaultConfigurationExtensions).GetMethod("BuildPaths", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ResolveOptionsMethod =
        typeof(LocalVaultConfigurationExtensions).GetMethod("ResolveOptions", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<string> BuildPaths(string project, LocalVaultOptions o)
        => ((IEnumerable<string>)BuildPathsMethod.Invoke(null, new object[] { project, o })!).ToList();

    private static LocalVaultOptions ResolveOptions(LocalVaultOptions seed)
        => (LocalVaultOptions)ResolveOptionsMethod.Invoke(null, new object?[] { seed })!;

    [Fact]
    public void BuildPaths_Minimal_DefaultsThenProject()
    {
        BuildPaths("web", new LocalVaultOptions())
            .Should().Equal("projects/defaults", "projects/web");
    }

    [Fact]
    public void BuildPaths_FullScope_OrdersDefaultsEnvProjectUser()
    {
        var o = new LocalVaultOptions { Domain = "acme", Environment = "prod", User = "jdoe" };

        BuildPaths("web", o).Should().Equal(
            "acme/projects/defaults",
            "acme/projects/defaults.prod",
            "acme/projects/web",
            "acme/projects/web.prod",
            "acme/users/jdoe/web",
            "acme/users/jdoe/web.prod");
    }

    [Fact]
    public void BuildPaths_EnvWithoutUser_OmitsUserPaths()
    {
        var o = new LocalVaultOptions { Environment = "dev" };
        BuildPaths("api", o).Should().Equal(
            "projects/defaults", "projects/defaults.dev", "projects/api", "projects/api.dev");
    }

    [Fact]
    public void ResolveOptions_LowercasesUserDomainEnvironment_AndKeepsSeededValues()
    {
        var resolved = ResolveOptions(new LocalVaultOptions
        {
            Token = "tok",
            Url = "http://vault:8200",
            User = "JDoe",
            Domain = "ACME",
            Environment = "PROD",
        });

        resolved.User.Should().Be("jdoe");
        resolved.Domain.Should().Be("acme");
        resolved.Environment.Should().Be("prod");
        resolved.Token.Should().Be("tok");        // seeded value wins over env/fallback
        resolved.Url.Should().Be("http://vault:8200");
    }
}
