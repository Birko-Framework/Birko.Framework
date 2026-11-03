using System;
using Birko.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Birko.Redis.Tests;

/// <summary>
/// TASK-707. <c>GetConnectionString()</c> appended the password, user name and client name as written, so a comma in
/// one of them started a new option: measured, the password <c>x,allowAdmin=true</c> parsed back as the password
/// <c>x</c> plus <c>allowAdmin=True</c>. StackExchange.Redis has no escape for a comma (its own
/// <c>ConfigurationOptions.ToString()</c> loses it the same way), so the connection manager now hands the driver a
/// <see cref="ConfigurationOptions"/> object, and the string form refuses a value it cannot carry.
/// </summary>
public class Task707RedisCredentialTests
{
    private static RedisSettings Injecting() => new("h", 6379, password: "x,allowAdmin=true")
    {
        UserName = "u,ssl=True",
        Name = "n,abortConnect=false",
    };

    [Fact]
    public void ConfigurationOptions_carry_values_with_commas_whole()
    {
        var o = Injecting().GetConfigurationOptions();

        o.Password.Should().Be("x,allowAdmin=true");
        o.User.Should().Be("u,ssl=True");
        o.ClientName.Should().Be("n,abortConnect=false");
        o.AllowAdmin.Should().BeFalse();
        o.Ssl.Should().BeFalse();
        o.AbortOnConnectFail.Should().Be(new ConfigurationOptions().AbortOnConnectFail);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("user")]
    [InlineData("name")]
    public void ConnectionString_refuses_a_value_it_cannot_carry(string field)
    {
        var s = new RedisSettings("h");
        switch (field)
        {
            case "password": s.Password = "x,allowAdmin=true"; break;
            case "user": s.UserName = "u,ssl=True"; break;
            default: s.Name = "n,abortConnect=false"; break;
        }

        var act = () => s.GetConnectionString();

        act.Should().Throw<ArgumentException>().WithMessage("*','*GetConfigurationOptions*");
    }

    [Fact]
    public void ConfigurationOptions_match_the_connection_string_for_plain_values()
    {
        var s = new RedisSettings("cache.host", 6380, password: "p=w", database: 3, useSsl: true)
        {
            UserName = "acl-user",
            Name = "svc",
        };

        var fromString = ConfigurationOptions.Parse(s.GetConnectionString()).ToString(includePassword: true);

        s.GetConfigurationOptions().ToString(includePassword: true).Should().Be(fromString);
    }

    [Fact]
    public void ConfigurationOptions_parse_a_raw_connection_string()
    {
        var o = new RedisSettings { RawConnectionString = "r1:6379,r2:6379,allowAdmin=true" }.GetConfigurationOptions();

        o.EndPoints.Should().HaveCount(2);
        o.AllowAdmin.Should().BeTrue();
    }

    [Fact]
    public void ConfigurationOptions_default_to_localhost()
    {
        new RedisSettings().GetConfigurationOptions().ToString().Should().Be("localhost:6379");
    }
}
