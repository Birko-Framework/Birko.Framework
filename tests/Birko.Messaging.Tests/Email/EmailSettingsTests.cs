using Birko.Messaging.Email;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Email;

public class EmailSettingsTests
{
    [Fact]
    public void Constructor_SetsHostAndPort()
    {
        var settings = new EmailSettings("smtp.example.com", 587);

        settings.Location.Should().Be("smtp.example.com");
        settings.Port.Should().Be(587);
    }

    [Fact]
    public void Constructor_WithCredentials_SetsUserNameAndPassword()
    {
        var settings = new EmailSettings("smtp.example.com", 587, "user", "pass");

        settings.UserName.Should().Be("user");
        settings.Password.Should().Be("pass");
    }

    [Fact]
    public void UseSecure_DefaultsToTrue()
    {
        var settings = new EmailSettings();

        settings.UseSecure.Should().BeTrue();
    }

    [Fact]
    public void Timeout_DefaultsTo30000()
    {
        var settings = new EmailSettings();

        settings.Timeout.Should().Be(30000);
    }

    [Fact]
    public void LoadFrom_CopiesAllProperties()
    {
        var source = new EmailSettings("smtp.test.com", 465, "admin", "secret")
        {
            UseSecure = false,
            Timeout = 60000,
            DefaultFrom = new MessageAddress("default@test.com", "Default Sender")
        };

        var target = new EmailSettings();
        target.LoadFrom(source);

        target.Location.Should().Be("smtp.test.com");
        target.Port.Should().Be(465);
        target.UserName.Should().Be("admin");
        target.Password.Should().Be("secret");
        target.UseSecure.Should().BeFalse();
        target.Timeout.Should().Be(60000);
        target.DefaultFrom.Should().NotBeNull();
        target.DefaultFrom!.Value.Should().Be("default@test.com");
    }
}
