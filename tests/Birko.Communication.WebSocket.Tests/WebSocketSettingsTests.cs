using Birko.Communication.WebSocket.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

public class WebSocketSettingsTests
{
    [Fact]
    public void DefaultUri_IsEmpty()
    {
        var settings = new WebSocketSettings();

        settings.Uri.Should().BeEmpty();
    }

    [Fact]
    public void GetID_ContainsWebSocket()
    {
        var settings = new WebSocketSettings { Name = "TestWS", Uri = "ws://localhost:8080" };

        settings.GetID().Should().Contain("WebSocket");
        settings.GetID().Should().Contain("TestWS");
        settings.GetID().Should().Contain("ws://localhost:8080");
    }

    [Fact]
    public void GetID_Format_IsPipeDelimited()
    {
        var settings = new WebSocketSettings { Name = "WS1", Uri = "ws://host" };

        settings.GetID().Should().Be("WebSocket|WS1|ws://host");
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var settings = new WebSocketSettings
        {
            Name = "Live Feed",
            Uri = "wss://api.example.com/ws"
        };

        settings.Name.Should().Be("Live Feed");
        settings.Uri.Should().Be("wss://api.example.com/ws");
    }
}
