using Birko.Configuration;
using FluentAssertions;
using Xunit;

namespace Birko.Configuration.Tests;

/// <summary>
/// Regressions for CR-H037 / CR-H038: the LoadFrom(Settings) overrides on PasswordSettings and
/// RemoteSettings were type-guarded and never called base, so loading from a less-derived Settings
/// silently dropped Location/Name (and Password).
/// </summary>
public class SettingsLoadFromTests
{
    [Fact]
    public void PasswordSettings_LoadFrom_PlainSettings_CopiesLocationAndName()
    {
        var target = new PasswordSettings { Password = "keep-me" };

        target.LoadFrom(new Settings("loc", "name")); // virtual LoadFrom(Settings) slot

        target.Location.Should().Be("loc", "CR-H037: base fields must be copied");
        target.Name.Should().Be("name");
    }

    [Fact]
    public void PasswordSettings_LoadFrom_PasswordSettings_CopiesEverything()
    {
        var target = new PasswordSettings();

        target.LoadFrom(new PasswordSettings("loc", "name", "pw"));

        target.Location.Should().Be("loc");
        target.Name.Should().Be("name");
        target.Password.Should().Be("pw");
    }

    [Fact]
    public void RemoteSettings_LoadFrom_PlainSettings_CopiesBaseFields()
    {
        var target = new RemoteSettings();

        target.LoadFrom(new Settings("loc", "name"));

        target.Location.Should().Be("loc", "CR-H038: base fields must be copied");
        target.Name.Should().Be("name");
    }

    [Fact]
    public void RemoteSettings_LoadFrom_PasswordSettings_CopiesLocationNameAndPassword()
    {
        var target = new RemoteSettings();

        target.LoadFrom(new PasswordSettings("loc", "name", "pw"));

        target.Location.Should().Be("loc");
        target.Name.Should().Be("name");
        target.Password.Should().Be("pw");
    }

    [Fact]
    public void RemoteSettings_LoadFrom_RemoteSettings_CopiesRemoteFields()
    {
        var target = new RemoteSettings();

        target.LoadFrom(new RemoteSettings("loc", "name", "user", "pw", 8443, useSecure: true));

        target.Location.Should().Be("loc");
        target.Name.Should().Be("name");
        target.Password.Should().Be("pw");
        target.UserName.Should().Be("user");
        target.Port.Should().Be(8443);
        target.UseSecure.Should().BeTrue();
    }
}
