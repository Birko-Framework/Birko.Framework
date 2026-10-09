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
    /// TASK-527: per-iteration and tool-call hooks on <see cref="Agent"/>, the readable system prompt,
    /// and <see cref="Agent.ResumeAsync"/>. Every loop test runs on the streaming and the
    /// non-streaming path — one loop serves both, and these pin that it stays so.
    /// </summary>
    public class AgentHooksTests
    {
        private const string Prompt = "test-prompt";

        private sealed record ProviderCall(bool Streaming, List<Message> Messages, string SystemPrompt, CancellationToken Token);

        private sealed class CapturingProvider : ILlmProvider
        {
            private readonly Queue<LlmResponse> _responses;
            private readonly List<string> _log;

            public CapturingProvider(List<string> log, params LlmResponse[] responses)
            {
                _log = log;
                _responses = new Queue<LlmResponse>(responses);
            }

            public List<ProviderCall> Calls { get; } = new();
            public string Name => "capturing";
            public Action<string, string>? MessageCallback { get; set; }

            private LlmResponse Record(bool streaming, List<Message> messages, string systemPrompt, CancellationToken token)
            {
                Calls.Add(new ProviderCall(streaming, new List<Message>(messages), systemPrompt, token));
                _log.Add($"provider:{Calls.Count}");
                return _responses.Count > 0 ? _responses.Dequeue() : EndTurn("default");
            }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => Task.FromResult(Record(false, messages, systemPrompt, cancellationToken));

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                var response = Record(true, messages, systemPrompt, cancellationToken);
                return Task.FromResult(new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromResult(Chunks()),
                    FinalResponse = response,
                    IsComplete = true
                });
            }

            private static async IAsyncEnumerable<string> Chunks()
            {
                await Task.Yield();
                yield return "chunk";
            }
        }

        private sealed class CapturingTool : Tool
        {
            private readonly List<string> _log;
            public CapturingTool(List<string> log) => _log = log;

            public Exception? Throw { get; set; }
            public int Executions { get; private set; }
            public List<CancellationToken> Tokens { get; } = new();
            public override string Name => "record";
            public override string Description => "records executions";
            public override object? InputSchema => null;

            public override async Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
            {
                Executions++;
                Tokens.Add(cancellationToken);
                _log.Add("tool");
                await Task.Delay(5, CancellationToken.None);
                if (Throw != null)
                    throw Throw;
                return "done";
            }
        }

        private sealed class RecordingHooks : AgentHooks
        {
            private readonly List<string> _log;
            public RecordingHooks(List<string> log) => _log = log;

            public Action<AgentIterationContext>? OnIteration { get; set; }
            public Action<ToolCallFinished>? OnFinished { get; set; }
            public List<AgentIterationContext> Contexts { get; } = new();
            public List<ToolCallStarted> Started { get; } = new();
            public List<ToolCallFinished> Finished { get; } = new();
            public List<CancellationToken> ToolHookTokens { get; } = new();

            public override ValueTask OnIterationStartingAsync(AgentIterationContext context)
            {
                Contexts.Add(context);
                _log.Add($"iteration:{context.Iteration}");
                OnIteration?.Invoke(context);
                return ValueTask.CompletedTask;
            }

            public override ValueTask OnToolCallStartingAsync(ToolCallStarted call, CancellationToken cancellationToken)
            {
                Started.Add(call);
                ToolHookTokens.Add(cancellationToken);
                _log.Add($"started:{call.ToolName}");
                return ValueTask.CompletedTask;
            }

            public override ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken)
            {
                Finished.Add(call);
                ToolHookTokens.Add(cancellationToken);
                _log.Add($"finished:{call.ToolName}");
                OnFinished?.Invoke(call);
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
            protected override string SystemPrompt => Prompt;
            protected override List<Tool> CreateTools() => _tools ?? new List<Tool>();
        }

        private sealed class Harness
        {
            public Harness(bool streaming, bool fallback, params LlmResponse[] responses)
            {
                Provider = new CapturingProvider(Log, responses);
                Tool = new CapturingTool(Log);
                Hooks = new RecordingHooks(Log);
                Agent = new TestAgent(Provider,
                    new AgentOptions { Verbose = false, EnableStreaming = streaming, StreamingFallbackToSync = fallback },
                    new List<Tool> { Tool })
                {
                    Hooks = Hooks
                };
            }

            public List<string> Log { get; } = new();
            public CapturingProvider Provider { get; }
            public CapturingTool Tool { get; }
            public RecordingHooks Hooks { get; }
            public TestAgent Agent { get; }
        }

        private static LlmResponse ToolUse(string tool, string id, Dictionary<string, object>? input = null) => new()
        {
            StopReason = "tool_use",
            Content = new List<ContentBlock>
            {
                new() { Type = "tool_use", Id = id, Name = tool, Input = input ?? new Dictionary<string, object>() }
            }
        };

        private static LlmResponse EndTurn(string text) => new()
        {
            StopReason = "end_turn",
            Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
        };

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Hooks_FireInLoopOrder(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("all done"));

            await h.Agent.RunAsync("do it");

            h.Log.Should().Equal(
                "iteration:1", "provider:1", "started:record", "tool", "finished:record",
                "iteration:2", "provider:2");
            h.Hooks.Contexts.Select(c => c.IsStreaming).Should().Equal(streaming, false);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_EndsTheRunBeforeTheModelCall(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), ToolUse("record", "t2"), EndTurn("never"));
            h.Hooks.OnIteration = c =>
            {
                if (c.Iteration == 2)
                    c.Stop("enough");
            };

            var conversation = await h.Agent.RunAsync("do it");

            h.Provider.Calls.Should().HaveCount(1);
            h.Tool.Executions.Should().Be(1);
            h.Hooks.Contexts[^1].StopReason.Should().Be("enough");
            conversation[^1].Role.Should().Be("user", "the tool_result turn is kept; no dangling tool_use");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_OnACancelledToken_ReturnsNormally(bool streaming)
        {
            var h = new Harness(streaming, fallback: true, EndTurn("never"));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            h.Hooks.OnIteration = c =>
            {
                if (c.CancellationToken.IsCancellationRequested)
                    c.Stop("cancelled");
            };

            var conversation = await h.Agent.RunAsync("do it", cancellationToken: cts.Token);

            conversation.Should().ContainSingle();
            h.Provider.Calls.Should().BeEmpty();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AppendedMessage_ReachesTheProvider_WithTheContextsSystemPrompt(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("done"));
            h.Hooks.OnIteration = c =>
            {
                if (c.Iteration == 2)
                    c.AppendUserMessage("reflect now");
            };

            await h.Agent.RunAsync("do it");

            var second = h.Provider.Calls[1];
            second.Messages[^1].Role.Should().Be("user");
            second.Messages[^1].Content.Should().Be("reflect now");
            second.SystemPrompt.Should().Be(h.Hooks.Contexts[1].SystemPrompt);
            h.Agent.CurrentSystemPrompt.Should().Be(Prompt);
            h.Hooks.Contexts[1].SystemPrompt.Should().Be(Prompt);
        }

        [Fact]
        public async Task AppendUserMessage_Blank_Throws()
        {
            AgentIterationContext? captured = null;
            var h = new Harness(false, fallback: false, EndTurn("done"));
            h.Hooks.OnIteration = c => captured = c;
            await h.Agent.RunAsync("do it");

            var act = () => captured!.AppendUserMessage("  ");

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ToolCallFinished_CarriesStructuredData(bool streaming)
        {
            var input = new Dictionary<string, object> { ["path"] = "a.cs" };
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1", input), EndTurn("done"));

            await h.Agent.RunAsync("do it");

            h.Hooks.Started.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Iteration = 1, ToolUseId = "t1", ToolName = "record" });
            h.Hooks.Started[0].Input.Should().ContainKey("path");
            var finished = h.Hooks.Finished.Should().ContainSingle().Subject;
            finished.ToolUseId.Should().Be("t1");
            finished.Input["path"].Should().Be("a.cs");
            finished.Result.Should().Be("done");
            finished.ToolFound.Should().BeTrue();
            finished.IsError.Should().BeFalse();
            finished.Exception.Should().BeNull();
            finished.Duration.Should().BePositive();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ToolCallFinished_UnknownTool_IsAnError(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("missing", "t1"), EndTurn("done"));

            await h.Agent.RunAsync("do it");

            var finished = h.Hooks.Finished.Should().ContainSingle().Subject;
            finished.ToolFound.Should().BeFalse();
            finished.IsError.Should().BeTrue();
            finished.Result.Should().StartWith("Error:");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ThrowingTool_FinishedHookFires_AndTheToolsExceptionPropagates(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("never"));
            var boom = new InvalidOperationException("boom");
            h.Tool.Throw = boom;

            var act = async () => await h.Agent.RunAsync("do it");

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(boom);
            var finished = h.Hooks.Finished.Should().ContainSingle().Subject;
            finished.Exception.Should().BeSameAs(boom);
            finished.IsError.Should().BeTrue();
            finished.Result.Should().BeNull();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ThrowingIterationHook_EndsTheRun_AndIsNeverRetriedBySyncFallback(bool streaming)
        {
            var h = new Harness(streaming, fallback: true, EndTurn("never"));
            var failure = new InvalidOperationException("hook failed");
            h.Hooks.OnIteration = _ => throw failure;

            var act = async () => await h.Agent.RunAsync("do it");

            var thrown = (await act.Should().ThrowAsync<AgentHookException>()).Which;
            thrown.HookName.Should().Be(nameof(IAgentHooks.OnIterationStartingAsync));
            thrown.Iteration.Should().Be(1);
            thrown.InnerException.Should().BeSameAs(failure);
            h.Provider.Calls.Should().BeEmpty();
            h.Hooks.Contexts.Should().ContainSingle("the hook ran once — no fallback re-run");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ThrowingFinishedHook_EndsTheRunWithAgentHookException(bool streaming)
        {
            var h = new Harness(streaming, fallback: true, ToolUse("record", "t1"), EndTurn("never"));
            h.Hooks.OnFinished = _ => throw new InvalidOperationException("hook failed");

            var act = async () => await h.Agent.RunAsync("do it");

            (await act.Should().ThrowAsync<AgentHookException>()).Which.HookName.Should().Be(nameof(IAgentHooks.OnToolCallFinishedAsync));
            h.Tool.Executions.Should().Be(1, "a hook failure is not retried, so the tool does not run again");
        }

        [Fact]
        public async Task ThrowingFinishedHook_WhileAToolExceptionIsInFlight_CarriesBoth()
        {
            var h = new Harness(false, fallback: false, ToolUse("record", "t1"));
            var toolFailure = new InvalidOperationException("tool failed");
            h.Tool.Throw = toolFailure;
            h.Hooks.OnFinished = _ => throw new ArgumentException("hook failed");

            var act = async () => await h.Agent.RunAsync("do it");

            var thrown = (await act.Should().ThrowAsync<AgentHookException>()).Which;
            thrown.InnerException.Should().BeOfType<ArgumentException>();
            thrown.ToolException.Should().BeSameAs(toolFailure);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationFromAHook_PropagatesUnwrapped(bool streaming)
        {
            var h = new Harness(streaming, fallback: true, EndTurn("never"));
            h.Hooks.OnIteration = _ => throw new OperationCanceledException();

            var act = async () => await h.Agent.RunAsync("do it");

            await act.Should().ThrowExactlyAsync<OperationCanceledException>();
            h.Provider.Calls.Should().BeEmpty();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TheRunsToken_ReachesProviderToolsAndHooks(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("done"));
            using var cts = new CancellationTokenSource();

            await h.Agent.RunAsync("do it", cancellationToken: cts.Token);

            h.Provider.Calls.Select(c => c.Streaming).Should().Equal(streaming, false);
            h.Provider.Calls.Should().OnlyContain(c => c.Token == cts.Token);
            h.Tool.Tokens.Should().ContainSingle().Which.Should().Be(cts.Token);
            h.Hooks.ToolHookTokens.Should().HaveCount(2).And.OnlyContain(t => t == cts.Token);
            h.Hooks.Contexts.Should().OnlyContain(c => c.CancellationToken == cts.Token);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ResumeAsync_SendsASavedConversationUnchanged(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, EndTurn("resumed"));
            var toolResult = new Message { Role = "user", Content = new List<object> { new { type = "tool_result", tool_use_id = "t1", content = "done" } } };
            var saved = new List<Message>
            {
                new() { Role = "user", Content = "do it" },
                new() { Role = "assistant", Content = ToolUse("record", "t1").Content },
                toolResult
            };

            var conversation = await h.Agent.ResumeAsync(saved);

            var first = h.Provider.Calls.Should().ContainSingle().Subject;
            first.Messages.Should().HaveCount(3);
            first.Messages[^1].Should().BeSameAs(toolResult, "no user turn is added on resume");
            saved.Should().HaveCount(3, "the caller's list is copied, not mutated");
            conversation.Should().HaveCount(4);
            conversation[^1].Role.Should().Be("assistant");
        }

        [Fact]
        public async Task ResumeAsync_EmptyConversation_Throws()
        {
            var h = new Harness(false, fallback: false);

            var act = async () => await h.Agent.ResumeAsync(new List<Message>());

            await act.Should().ThrowAsync<ArgumentException>();
            h.Provider.Calls.Should().BeEmpty();
        }

        [Fact]
        public async Task ResumeAsync_TrailingAssistantTurn_Throws()
        {
            var h = new Harness(false, fallback: false);
            var saved = new List<Message>
            {
                new() { Role = "user", Content = "do it" },
                new() { Role = "assistant", Content = EndTurn("done").Content }
            };

            var act = async () => await h.Agent.ResumeAsync(saved);

            await act.Should().ThrowAsync<ArgumentException>();
            h.Provider.Calls.Should().BeEmpty();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NoHooks_RunsAsBefore(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("done"));
            h.Agent.Hooks = null;

            var conversation = await h.Agent.RunAsync("do it");

            h.Tool.Executions.Should().Be(1);
            h.Provider.Calls.Should().HaveCount(2);
            conversation.Should().HaveCount(4);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OnLlmResponseReceived_FiresOncePerModelCall(bool streaming)
        {
            var h = new Harness(streaming, fallback: false, ToolUse("record", "t1"), EndTurn("done"));
            var fired = new List<int>();
            h.Agent.Options.OnLlmResponseReceived = () => fired.Add(h.Provider.Calls.Count);

            await h.Agent.RunAsync("do it");

            fired.Should().Equal(1, 2);
        }

        [Fact]
        public async Task NoHooks_ThrowingTool_PropagatesItsException()
        {
            var h = new Harness(false, fallback: false, ToolUse("record", "t1"));
            h.Agent.Hooks = null;
            h.Tool.Throw = new InvalidOperationException("boom");

            var act = async () => await h.Agent.RunAsync("do it");

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }
    }
}
