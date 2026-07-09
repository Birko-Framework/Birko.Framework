using Birko.AI.Models;
using Birko.AI.Tools;

namespace Birko.AI.Providers
{
    public interface ILlmProvider
    {
        string Name { get; }
        Action<string, string>? MessageCallback { get; set; }
        Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default);
        Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default);
    }
}
