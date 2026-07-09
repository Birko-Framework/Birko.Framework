using Birko.AI;
using Birko.AI.Agents;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Agents.Tests
{
    /// <summary>
    /// Regression coverage for the agent run loop (CR-M005 test-gap), exercising the CR-M001 fix
    /// (HandleResponse is now async — no GetAwaiter().GetResult() bridge) and CR-M002 (the run
    /// observes a CancellationToken).
    /// </summary>
    public class AgentLoopTests
    {
        private sealed class ScriptedProvider : ILlmProvider
        {
            private readonly Queue<LlmResponse> _responses;
            public int SyncCalls { get; private set; }
            public ScriptedProvider(params LlmResponse[] responses) => _responses = new Queue<LlmResponse>(responses);

            public string Name => "scripted";
            public Action<string, string>? MessageCallback { get; set; }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SyncCalls++;
                var next = _responses.Count > 0 ? _responses.Dequeue() : new LlmResponse { StopReason = "end_turn", Content = [] };
                return Task.FromResult(next);
            }

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        private sealed class RecordingTool : Tool
        {
            public int Executions { get; private set; }
            public override string Name => "record";
            public override string Description => "records executions";
            public override object? InputSchema => null;
            public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
            {
                Executions++;
                return Task.FromResult("done");
            }
        }

        private sealed class TestAgent : Agent
        {
            private readonly List<Tool> _tools;
            public TestAgent(ILlmProvider provider, AgentOptions options, List<Tool> tools)
                : base(provider, options)
            {
                _tools = tools;
                RebuildTools();
            }
            protected override string SystemPrompt => "test";
            protected override List<Tool> CreateTools() => _tools ?? new List<Tool>();
            public string PublicDepthGuidance() => GetDepthGuidance();
        }

        private static LlmResponse ToolUse(string tool, string id) => new()
        {
            StopReason = "tool_use",
            Content = new List<ContentBlock>
            {
                new() { Type = "tool_use", Id = id, Name = tool, Input = new Dictionary<string, object>() }
            }
        };

        private static LlmResponse EndTurn(string text) => new()
        {
            StopReason = "end_turn",
            Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
        };

        [Fact]
        public async Task RunAsync_ExecutesTool_ThenCompletes()
        {
            // tool_use on iteration 1, end_turn on iteration 2 — exercises the (now async)
            // HandleResponse -> HandleToolUse -> tool.ExecuteAsync path.
            var provider = new ScriptedProvider(ToolUse("record", "t1"), EndTurn("all done"));
            var tool = new RecordingTool();
            var agent = new TestAgent(provider, new AgentOptions { Verbose = false }, new List<Tool> { tool });

            var conversation = await agent.RunAsync("do it");

            tool.Executions.Should().Be(1);
            provider.SyncCalls.Should().Be(2);
            conversation.Should().Contain(m => m.Role == "assistant");
        }

        [Fact]
        public async Task RunAsync_PreCancelledToken_ThrowsBeforeCallingProvider()
        {
            var provider = new ScriptedProvider(EndTurn("never"));
            var agent = new TestAgent(provider, new AgentOptions { Verbose = false }, new List<Tool>());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await agent.RunAsync("do it", cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            provider.SyncCalls.Should().Be(0);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(9)]
        public void DepthGuidance_ReturnsNonEmpty_ForEachBand(int depth)
        {
            var agent = new TestAgent(new ScriptedProvider(), new AgentOptions { ModelDepth = depth }, new List<Tool>());
            agent.PublicDepthGuidance().Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void DepthGuidance_DiffersAcrossBands()
        {
            var quick = new TestAgent(new ScriptedProvider(), new AgentOptions { ModelDepth = 2 }, new List<Tool>()).PublicDepthGuidance();
            var balanced = new TestAgent(new ScriptedProvider(), new AgentOptions { ModelDepth = 5 }, new List<Tool>()).PublicDepthGuidance();
            var deep = new TestAgent(new ScriptedProvider(), new AgentOptions { ModelDepth = 9 }, new List<Tool>()).PublicDepthGuidance();

            quick.Should().NotBe(balanced);
            balanced.Should().NotBe(deep);
            quick.Should().NotBe(deep);
        }
    }
}
