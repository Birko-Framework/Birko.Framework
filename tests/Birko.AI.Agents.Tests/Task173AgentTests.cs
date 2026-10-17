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
    /// TASK-173 — the low findings in the agent loop and the factory: SH-L156, L158, L160, L164, L165.
    /// </summary>
    public class Task173AgentTests
    {
        private sealed class Provider : ILlmProvider
        {
            private readonly Queue<LlmResponse> _responses;
            public Provider(params LlmResponse[] responses) => _responses = new(responses);
            public List<List<Tool>> ToolLists { get; } = [];
            public string? StreamingError { get; init; }
            public string Name => "p";
            public Action<string, string>? MessageCallback { get; set; }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                ToolLists.Add(tools);
                return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new LlmResponse { StopReason = "end_turn", Content = [] });
            }

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => Task.FromResult(new LlmStreamingResponse
                {
                    GetStreamAsync = () => throw new InvalidOperationException("no stream"),
                    Error = StreamingError,
                });
        }

        private sealed class TestAgent : Agent
        {
            private readonly List<Tool> _tools;
            public TestAgent(ILlmProvider provider, AgentOptions options, List<Tool> tools) : base(provider, options)
            {
                _tools = tools;
                RebuildTools();
            }
            protected override string SystemPrompt => "s";
            protected override List<Tool> CreateTools() => _tools ?? [];
        }

        private sealed class Callback(Action run) : Tool
        {
            public override string Name => "callback";
            public override string Description => "runs an action";
            public override object? InputSchema => null;
            public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
            {
                run();
                return Task.FromResult("ok");
            }
        }

        private static LlmResponse ToolUse(string name) => new()
        {
            StopReason = "tool_use",
            Content = [new ContentBlock { Type = "tool_use", Id = "t1", Name = name, Input = new() }],
        };

        [Fact]
        public async Task SH_L156_a_failed_stream_is_not_counted_as_a_response_and_the_fallback_counts_once()
        {
            var count = 0;
            var provider = new Provider(new LlmResponse { StopReason = "end_turn", Content = [new ContentBlock { Type = "text", Text = "ok" }] })
            {
                StreamingError = "Streaming not supported",
            };
            var agent = new TestAgent(provider, new AgentOptions { Verbose = false, EnableStreaming = true, StreamingFallbackToSync = true, OnLlmResponseReceived = () => count++ }, []);

            await agent.RunAsync("go");

            count.Should().Be(1, "one logical response: the failed stream is not one, the sync fallback is");
        }

        [Fact]
        public async Task SH_L158_a_tool_added_during_a_run_does_not_mutate_the_list_the_provider_was_given()
        {
            TestAgent? agent = null;
            var provider = new Provider(ToolUse("callback"));
            agent = new TestAgent(provider, new AgentOptions { Verbose = false },
                [new Callback(() => agent!.AddTool(new Callback(() => { })))]);

            await agent.RunAsync("go");

            provider.ToolLists[0].Should().HaveCount(1, "the first call's tool list is a snapshot");
            provider.ToolLists[^1].Should().HaveCount(2);
        }

        [Fact]
        public void SH_L158_adding_a_null_tool_is_refused()
        {
            var agent = new TestAgent(new Provider(), new AgentOptions(), []);
            ((Action)(() => agent.AddTool(null!))).Should().Throw<ArgumentNullException>();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task SH_L160_a_run_with_no_iterations_is_refused_rather_than_returning_nothing(int max)
        {
            var provider = new Provider();
            var act = () => new TestAgent(provider, new AgentOptions { Verbose = false, MaxIterations = max }, []).RunAsync("go");
            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
            provider.ToolLists.Should().BeEmpty();
        }

        [Fact]
        public void SH_L164_an_alias_whose_target_is_missing_names_the_target()
        {
            var alias = "t173-docs-" + Guid.NewGuid().ToString("N")[..6];
            AgentFactory.RegisterAlias(alias, "t173-no-such-writer");

            var act = () => AgentFactory.Create(new Provider(), agentType: alias);

            act.Should().Throw<ArgumentException>().WithMessage($"*'{alias}'*'t173-no-such-writer'*");
        }

        [Fact]
        public void SH_L165_a_null_agent_type_is_an_argument_exception_naming_the_parameter()
        {
            var act = () => AgentFactory.Create(new Provider(), agentType: null!);
            act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "agentType");
        }
    }
}
