using Birko.Communication.GraphQL;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLSettingsTests
{
    [Fact]
    public void Endpoint_MapsToLocation()
    {
        var settings = new GraphQLSettings { Endpoint = "https://api.example.com/graphql" };

        settings.Location.Should().Be("https://api.example.com/graphql");
    }

    [Fact]
    public void Location_ReturnsEndpoint()
    {
        var settings = new GraphQLSettings { Location = "https://api.example.com/graphql" };

        settings.Endpoint.Should().Be("https://api.example.com/graphql");
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new GraphQLSettings();

        settings.SchemaPath.Should().Be("/graphql");
        settings.UseSubscriptions.Should().BeFalse();
        settings.SubscriptionProtocol.Should().Be(GraphQLSubscriptionProtocol.WebSocket);
        settings.TimeoutSeconds.Should().Be(30);
        settings.EnableAutoPersistedQueries.Should().BeFalse();
        settings.ExtraHeaders.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RemoteSettings_Inheritance_Works()
    {
        var settings = new GraphQLSettings
        {
            Endpoint = "https://api.example.com/graphql",
            UserName = "user",
            Password = "pass",
            Port = 443,
            UseSecure = true
        };

        settings.UserName.Should().Be("user");
        settings.Password.Should().Be("pass");
        settings.Port.Should().Be(443);
        settings.UseSecure.Should().BeTrue();
    }
}
