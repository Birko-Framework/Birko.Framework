using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// GLM-5.3 gets its documented output budget, and a reply cut off by the token limit is reported as such (TASK-516).
/// </summary>
public class ZAiProviderTests
{
    private sealed class CannedHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private static string Reply(string content, string finishReason) =>
        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(content) +
        "},\"finish_reason\":\"" + finishReason + "\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":20}}";

    private static Task<LlmResponse> SendAsync(ZAiProvider provider) =>
        provider.SendMessageAsync(new List<Message> { new() { Role = "user", Content = "hi" } }, new List<Tool>(), "system");

    [Theory]
    [InlineData("glm-5.3", 131072)]
    [InlineData("glm-5.3-flash", 65536)]
    public async Task Glm53_requests_its_output_budget_not_the_4096_default(string model, int expected)
    {
        var handler = new CannedHandler(Reply("ok", "stop"));
        var provider = new ZAiProvider("key", model, baseUrl: "https://example.test/v4", handler: handler);

        await SendAsync(provider);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(expected);
    }

    [Fact]
    public async Task A_reply_cut_off_by_the_token_limit_reports_max_tokens()
    {
        var provider = new ZAiProvider("key", "glm-5.3", baseUrl: "https://example.test/v4", handler: new CannedHandler(Reply("", "length")));

        var response = await SendAsync(provider);

        response.StopReason.Should().Be("max_tokens");
    }

    [Fact]
    public async Task A_finished_reply_still_reports_end_turn()
    {
        var provider = new ZAiProvider("key", "glm-5.3", baseUrl: "https://example.test/v4", handler: new CannedHandler(Reply("done", "stop")));

        var response = await SendAsync(provider);

        response.StopReason.Should().Be("end_turn");
        response.Content.Should().ContainSingle(b => b.Text == "done");
    }
}
