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
    /// TASK-544: <see cref="AgentOptions.StreamingFallbackToSync"/> retries the streaming request — iteration 1's
    /// model call — and nothing else. It used to wrap the whole run, so a failure in a later iteration or a
    /// throwing tool replayed the run from the start and every tool that had already executed ran again.
    /// </summary>
    public class AgentStreamingFallbackTests
    {
        public enum StreamFailure { None, Throws, ErrorField, MidStream, Cancelled }

        // Each model call is scripted by its 1-based call number; a step either answers or throws.
        private sealed class ScriptedProvider : ILlmProvider
        {
            private readonly Func<int, LlmResponse> _script;
            public ScriptedProvider(Func<int, LlmResponse> script) => _script = script;

            public StreamFailure StreamFailure { get; set; }
            public List<(bool Streaming, int MessageCount)> Calls { get; } = new();
            public string Name => "scripted";
            public Action<string, string>? MessageCallback { get; set; }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                Calls.Add((false, messages.Count));
                return Task.FromResult(_script(Calls.Count));
            }

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                Calls.Add((true, messages.Count));
                if (StreamFailure == StreamFailure.Throws)
                    throw new HttpRequestException("stream refused");
                if (StreamFailure == StreamFailure.Cancelled)
                    throw new OperationCanceledException("stream cancelled");
                if (StreamFailure == StreamFailure.ErrorField)
                    return Task.FromResult(new LlmStreamingResponse { Error = "overloaded", GetStreamAsync = () => Task.FromResult(Chunks(false)) });

                var response = _script(Calls.Count);
                var midStream = StreamFailure == StreamFailure.MidStream;
                return Task.FromResult(new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromResult(Chunks(midStream)),
                    FinalResponse = response,
                    IsComplete = true
                });
            }

            private static async IAsyncEnumerable<string> Chunks(bool fail)
            {
                await Task.Yield();
                yield return "chunk";
                if (fail)
                    throw new IOException("connection reset");
            }
        }

        private sealed class CountingTool : Tool
        {
            public Exception? Throw { get; set; }
            public int Executions { get; private set; }
            public override string Name => "record";
            public override string Description => "counts executions";
            public override object? InputSchema => null;

            public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
            {
                Executions++;
                if (Throw != null)
                    throw Throw;
                return Task.FromResult("done");
            }
        }

        private sealed class CountingHooks : AgentHooks
        {
            public List<int> Iterations { get; } = new();
            public int Started { get; private set; }
            public int Finished { get; private set; }
            public string? Append { get; set; }

            public override ValueTask OnIterationStartingAsync(AgentIterationContext context)
            {
                Iterations.Add(context.Iteration);
                if (Append != null && context.Iteration == 1)
                    context.AppendUserMessage(Append);
                return ValueTask.CompletedTask;
            }

            public override ValueTask OnToolCallStartingAsync(ToolCallStarted call, CancellationToken cancellationToken)
            {
                Started++;
                return ValueTask.CompletedTask;
            }

            public override ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken)
            {
                Finished++;
                return ValueTask.CompletedTask;
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
            protected override string SystemPrompt => "test-prompt";
            protected override List<Tool> CreateTools() => _tools ?? new List<Tool>();
        }

        private sealed class Harness
        {
            public Harness(Func<int, LlmResponse> script, bool fallback = true)
            {
                Provider = new ScriptedProvider(script);
                Agent = new TestAgent(Provider,
                    new AgentOptions { Verbose = false, EnableStreaming = true, StreamingFallbackToSync = fallback },
                    new List<Tool> { Tool })
                {
                    Hooks = Hooks
                };
                Agent.SetMessageCallback((type, text) => Messages.Add((type, text)));
            }

            public ScriptedProvider Provider { get; }
            public CountingTool Tool { get; } = new();
            public CountingHooks Hooks { get; } = new();
            public TestAgent Agent { get; }
            public List<(string Type, string Text)> Messages { get; } = new();
        }

        private static LlmResponse ToolUse(string id) => new()
        {
            StopReason = "tool_use",
            Content = new List<ContentBlock>
            {
                new() { Type = "tool_use", Id = id, Name = "record", Input = new Dictionary<string, object>() }
            }
        };

        private static LlmResponse EndTurn(string text) => new()
        {
            StopReason = "end_turn",
            Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
        };

        [Fact]
        public async Task LaterIterationFailure_Propagates_AndNoToolRunsTwice()
        {
            var failure = new HttpRequestException("provider down");
            var h = new Harness(call => call switch
            {
                1 => ToolUse("t1"),
                2 => throw failure,
                3 => ToolUse("t1"),
                _ => EndTurn("done")
            });

            var act = async () => await h.Agent.RunAsync("do it");

            (await act.Should().ThrowAsync<HttpRequestException>()).Which.Should().BeSameAs(failure);
            h.Tool.Executions.Should().Be(1, "the tool executed in iteration 1 must not be replayed");
            h.Provider.Calls.Select(c => c.Streaming).Should().Equal(true, false);
            h.Hooks.Iterations.Should().Equal(1, 2);
            h.Hooks.Started.Should().Be(1);
            h.Hooks.Finished.Should().Be(1);
            h.Messages.Should().NotContain(m => m.Text.StartsWith("Streaming failed"));
        }

        [Fact]
        public async Task ThrowingTool_InAStreamingRun_Propagates_WithoutAFallback()
        {
            var boom = new InvalidOperationException("tool failed");
            var h = new Harness(call => call == 1 ? ToolUse("t1") : EndTurn("done"));
            h.Tool.Throw = boom;

            var act = async () => await h.Agent.RunAsync("do it");

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(boom);
            h.Tool.Executions.Should().Be(1);
            h.Provider.Calls.Should().ContainSingle().Which.Streaming.Should().BeTrue();
            h.Hooks.Iterations.Should().Equal(1);
        }

        [Theory]
        [InlineData(StreamFailure.Throws)]
        [InlineData(StreamFailure.ErrorField)]
        [InlineData(StreamFailure.MidStream)]
        public async Task StreamingRequestFailure_RetriesThatCallWithoutStreaming_AndContinues(StreamFailure failure)
        {
            var h = new Harness(call => call <= 2 ? ToolUse("t1") : EndTurn("done"));
            h.Provider.StreamFailure = failure;
            h.Hooks.Append = "steer";

            var conversation = await h.Agent.RunAsync("do it");

            h.Messages.Should().Contain(m => m.Type == "warning" && m.Text.StartsWith("Streaming failed"));
            h.Provider.Calls.Select(c => c.Streaming).Should().Equal(true, false, false);
            h.Provider.Calls[1].MessageCount.Should().Be(h.Provider.Calls[0].MessageCount,
                "the failed attempt appended nothing — the retry sends the same conversation");
            h.Tool.Executions.Should().Be(1);
            h.Hooks.Iterations.Should().Equal(1, 2);
            h.Hooks.Started.Should().Be(1);
            conversation.Count(m => m.Role == "user" && m.Content is string s && s == "steer")
                .Should().Be(1, "the hook's appended message is not added twice");
            conversation[^1].Role.Should().Be("assistant");
        }

        [Fact]
        public async Task StreamingRequestFailure_WithFallbackOff_Propagates()
        {
            var h = new Harness(_ => EndTurn("done"), fallback: false);
            h.Provider.StreamFailure = StreamFailure.Throws;

            var act = async () => await h.Agent.RunAsync("do it");

            await act.Should().ThrowAsync<HttpRequestException>();
            h.Provider.Calls.Should().ContainSingle();
        }

        [Fact]
        public async Task CancellationDuringTheStream_IsNotConvertedIntoAFallback()
        {
            var h = new Harness(_ => EndTurn("done"));
            h.Provider.StreamFailure = StreamFailure.Cancelled;

            var act = async () => await h.Agent.RunAsync("do it");

            await act.Should().ThrowAsync<OperationCanceledException>();
            h.Provider.Calls.Should().ContainSingle().Which.Streaming.Should().BeTrue();
        }
    }
}
