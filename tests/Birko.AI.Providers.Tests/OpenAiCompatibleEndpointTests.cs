using System.Reflection;
using Birko.AI.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// Regression for CR-H004: OpenAiCompatibleProviderBase appended "/v1/chat/completions" to
/// BaseUrl unconditionally, so providers whose default baseUrl already ended in the full
/// endpoint path (Mistral/DeepSeek/Groq/OpenRouter/LmStudio) produced a doubled
/// ".../v1/chat/completions/v1/chat/completions" that 404s. The base now only appends when the
/// URL isn't already a chat-completions endpoint.
/// </summary>
public class OpenAiCompatibleEndpointTests
{
    private static string ResolveUrl(OpenAiCompatibleProviderBase provider)
    {
        var prop = typeof(OpenAiCompatibleProviderBase)
            .GetProperty("ChatCompletionsUrl", BindingFlags.NonPublic | BindingFlags.Instance);
        prop.Should().NotBeNull();
        return (string)prop!.GetValue(provider)!;
    }

    public static IEnumerable<object[]> Providers => new[]
    {
        new object[] { new MistralProvider("k"),   "https://api.mistral.ai/v1/chat/completions" },
        new object[] { new DeepSeekProvider("k"),  "https://api.deepseek.com/v1/chat/completions" },
        new object[] { new GroqProvider("k"),      "https://api.groq.com/openai/v1/chat/completions" },
        new object[] { new OpenRouterProvider("k"),"https://openrouter.ai/api/v1/chat/completions" },
        new object[] { new LmStudioProvider(),     "http://localhost:1234/v1/chat/completions" },
        new object[] { new VllmProvider(),         "http://localhost:8000/v1/chat/completions" },
        new object[] { new SglangProvider(),       "http://localhost:30000/v1/chat/completions" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void DefaultEndpoint_IsSingleChatCompletionsPath(OpenAiCompatibleProviderBase provider, string expected)
    {
        var url = ResolveUrl(provider);

        url.Should().Be(expected);
        // Must never double the endpoint path.
        CountOccurrences(url, "/chat/completions").Should().Be(1);
    }

    [Fact]
    public void CustomBareHost_GetsEndpointAppended()
    {
        var url = ResolveUrl(new VllmProvider(baseUrl: "http://example.com:9000"));

        url.Should().Be("http://example.com:9000/v1/chat/completions");
    }

    [Fact]
    public void CustomFullPathUrl_IsNotDoubled()
    {
        var url = ResolveUrl(new VllmProvider(baseUrl: "http://example.com/v1/chat/completions"));

        url.Should().Be("http://example.com/v1/chat/completions");
        CountOccurrences(url, "/chat/completions").Should().Be(1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
