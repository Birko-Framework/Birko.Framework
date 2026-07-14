using Birko.Configuration;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Configuration.Tests;

/// <summary>
/// GetId composition across the settings hierarchy (CR-M077) and the LoadFrom(ISettings) entry
/// point (CR-M076: it used to hard-cast to Settings and throw InvalidCastException for any foreign
/// ISettings implementation; it is now type-guarded).
/// </summary>
public class SettingsTests
{
    /// <summary>A foreign ISettings that does NOT derive from Settings — the CR-M076 hazard.</summary>
    private sealed class ForeignSettings : ISettings
    {
        public string GetId() => "foreign";
        public void LoadFrom(ISettings data) { }
    }

    [Fact]
    public void GetId_Settings_composes_location_and_name()
    {
        new Settings("srv", "db").GetId().Should().Be("srv:db");
    }

    [Fact]
    public void GetId_PasswordSettings_uses_base_format()
    {
        // PasswordSettings does not override GetId — same shape as Settings.
        new PasswordSettings("srv", "db", "pw").GetId().Should().Be("srv:db");
    }

    [Fact]
    public void GetId_RemoteSettings_appends_username_and_port()
    {
        var settings = new RemoteSettings("srv", "db", "user", "pw", 5432);

        settings.GetId().Should().Be("srv:db:user:5432");
    }

    [Fact]
    public void LoadFrom_ISettings_with_foreign_implementation_does_not_throw()
    {
        // CR-M076: (Settings)data threw InvalidCastException here. It must now be a safe no-op.
        var target = new Settings("keep-loc", "keep-name");

        var act = () => target.LoadFrom((ISettings)new ForeignSettings());

        act.Should().NotThrow();
        target.Location.Should().Be("keep-loc");
        target.Name.Should().Be("keep-name");
    }

    [Fact]
    public void LoadFrom_ISettings_with_Settings_instance_copies_values()
    {
        var target = new Settings();

        target.LoadFrom((ISettings)new Settings("srv", "db"));

        target.Location.Should().Be("srv");
        target.Name.Should().Be("db");
    }

    [Fact]
    public void LoadFrom_ISettings_with_null_does_not_throw()
    {
        var target = new Settings("loc", "name");

        var act = () => target.LoadFrom((ISettings)null!);

        act.Should().NotThrow();
    }

    [Fact]
    public void PasswordSettings_WithoutPassword_DefaultsToEmptyNotNull()
    {
        // Regression for CR-L094: the ctor laundered null through `password ?? null!`, leaving a null
        // Password the type system claimed was non-null. It now defaults to string.Empty.
        new PasswordSettings("srv", "db").Password.Should().NotBeNull().And.BeEmpty();
        new PasswordSettings().Password.Should().NotBeNull().And.BeEmpty();
        new PasswordSettings("srv", "db", "pw").Password.Should().Be("pw");
    }
}
