using Birko.AI;
using Birko.AI.Agents;
using Birko.AI.Factories;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Agents.Tests
{
    /// <summary>
    /// TASK-160 — the agent-loop and factory findings: SH-M153 (tool failure detection), SH-M154 (an empty tool_result
    /// turn), SH-M155 (the shared provider's callback) and SH-M156 (AgentFactory under concurrency).
    /// </summary>
    public class Task160AgentTests
    {
        private sealed class Scripted(params LlmResponse[] responses) : ILlmProvider
        {
            private readonly Queue<LlmResponse> _responses = new(responses);
            public List<List<Message>> Calls { get; } = [];
            public string Name => "scripted";
            public Action<string, string>? MessageCallback { get; set; }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                Calls.Add(messages.ToList());
                return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new LlmResponse { StopReason = "end_turn", Content = [] });
            }

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        private sealed class TestAgent : Agent
        {
            private readonly List<Tool> _tools;
            public TestAgent(ILlmProvider provider, List<Tool> tools, Action<string, string>? callback = null)
                : base(provider, new AgentOptions { Verbose = false }, callback)
            {
                _tools = tools;
                RebuildTools();
            }
            protected override string SystemPrompt => "system";
            protected override List<Tool> CreateTools() => _tools ?? [];
        }

        [Fact]
        public async Task SH_M154_a_tool_use_stop_with_no_tool_call_ends_the_run_without_an_empty_user_turn()
        {
            var provider = new Scripted(new LlmResponse
            {
                StopReason = "tool_use",
                Content = [new ContentBlock { Type = "text", Text = "I would call a tool" }],
            });

            var conversation = await new TestAgent(provider, []).RunAsync("go");

            provider.Calls.Should().ContainSingle("the run ends instead of sending a fabricated empty turn");
            conversation.Where(m => m.Role == "user" && m.Content is List<object> items && items.Count == 0).Should().BeEmpty();
            conversation[^1].Role.Should().Be("assistant");
        }

        [Fact]
        public void SH_M155_an_agent_without_a_callback_leaves_the_providers_callback_alone()
        {
            var calls = new List<string>();
            var provider = new Scripted { MessageCallback = (type, _) => calls.Add(type) };

            _ = new TestAgent(provider, []);

            provider.MessageCallback.Should().NotBeNull("constructing an agent with no callback must not wipe the provider's sink");
        }

        [Fact]
        public void SH_M155_an_agent_with_a_callback_still_routes_the_providers_messages_to_it()
        {
            var provider = new Scripted();
            Action<string, string> mine = (_, _) => { };
            _ = new TestAgent(provider, [], mine);
            provider.MessageCallback.Should().BeSameAs(mine);
        }

        [Fact]
        public void SH_M153_every_built_in_tool_reports_a_failure_with_the_error_prefix_the_agent_counts()
        {
            var dir = Directory.CreateTempSubdirectory("birko_tools_").FullName;
            try
            {
                var file = Path.Combine(dir, "a.txt");
                File.WriteAllText(file, "x");
                var cases = new (Tool Tool, Dictionary<string, object> Input)[]
                {
                    (new ReadFileTool(), new()),
                    (new WriteFileTool(), new()),
                    (new EditFileTool(), new()),
                    (new AppendToFileTool(), new()),
                    (new SearchCodeTool(), new()),
                    (new RunCommandTool(), new()),
                    (new ListFilesTool(), new() { ["directory"] = "a.txt" }), // a file, not a directory
                };
                foreach (var (tool, input) in cases)
                {
                    var result = tool.ExecuteAsync(dir, input).GetAwaiter().GetResult();
                    result.Should().StartWith("Error:", $"{tool.Name} failed and must say so in the form the agent counts");
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task SH_M156_the_factory_survives_registration_racing_creation_and_enumeration()
        {
            var prefix = "task160-" + Guid.NewGuid().ToString("N")[..8] + "-";
            AgentFactory.Register(prefix + "base", (p, o) => new TestAgent(p, []));
            var provider = new Scripted();

            var act = async () => await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 500; i++)
                {
                    AgentFactory.Register($"{prefix}{t}-{i}", (p, o) => new TestAgent(p, []));
                    AgentFactory.RegisterAlias($"{prefix}alias-{t}-{i}", prefix + "base");
                    AgentFactory.Create(provider, agentType: prefix + "base").Should().NotBeNull();
                    foreach (var _ in AgentFactory.GetRegisteredAgentTypes()) { }
                    foreach (var _ in AgentFactory.GetRegisteredAliases()) { }
                }
            })));

            await act.Should().NotThrowAsync();
            AgentFactory.IsRegistered($"{prefix}7-499").Should().BeTrue();
        }
    }
}
