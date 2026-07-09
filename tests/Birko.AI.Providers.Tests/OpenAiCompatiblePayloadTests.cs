using System.Reflection;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// Part of CR-H006 (test-gap): the OpenAI-compatible providers had zero coverage. This asserts
/// the request payload shape (model / max_tokens / stream / messages) that the API contract
/// depends on, exercised without a live network call.
/// </summary>
public class OpenAiCompatiblePayloadTests
{
    private static JsonElement BuildPayload(OpenAiCompatibleProviderBase provider, bool streaming)
    {
        var name = streaming ? "BuildStreamingPayload" : "BuildPayload";
        var method = typeof(OpenAiCompatibleProviderBase)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"{name} should exist");

        var messages = new List<Message> { new() { Role = "user", Content = "hi" } };
        var payload = method!.Invoke(provider, new object[] { messages, new List<Tool>(), "system" })!;
        return JsonSerializer.SerializeToElement(payload);
    }

    [Fact]
    public void BuildPayload_HasModelAndMaxTokens()
    {
        var element = BuildPayload(new VllmProvider(model: "my-model"), streaming: false);

        element.GetProperty("model").GetString().Should().Be("my-model");
        element.TryGetProperty("max_tokens", out _).Should().BeTrue();
        element.TryGetProperty("messages", out var messages).Should().BeTrue();
        messages.GetArrayLength().Should().BeGreaterThan(0);
        element.GetProperty("stream").GetBoolean().Should().BeFalse();
    }
}
