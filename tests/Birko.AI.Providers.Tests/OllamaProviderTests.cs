using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// Regression test for CR-C01: the Ollama streaming path posted to the base URL (server root)
/// instead of the /api/chat endpoint, so every streaming call 404'd.
/// </summary>
public class OllamaProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            // Minimal Ollama /api/chat NDJSON so the streaming setup succeeds.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":{\"content\":\"\"},\"done\":true}\n")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task StreamingRequest_PostsToChatEndpoint_NotBaseUrl()
    {
        var handler = new CapturingHandler();
        var provider = new OllamaProvider("llama3.2", "http://localhost:11434", handler);

        await provider.SendMessageStreamingAsync(new List<Message>(), new List<Tool>(), systemPrompt: "");

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.AbsoluteUri.Should().Be("http://localhost:11434/api/chat");
    }

    [Fact]
    public async Task NonStreamingRequest_PostsToChatEndpoint()
    {
        var handler = new CapturingHandler();
        var provider = new OllamaProvider("llama3.2", "http://localhost:11434", handler);

        await provider.SendMessageAsync(new List<Message>(), new List<Tool>(), systemPrompt: "");

        handler.LastRequestUri!.AbsoluteUri.Should().Be("http://localhost:11434/api/chat");
    }
}
