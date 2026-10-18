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
    /// TASK-660 (SH-L159): a run's outcome can be told apart without matching strings. Decided by the user: new
    /// Run/Continue/ResumeWithOutcomeAsync return an <see cref="AgentRunResult"/>; the conversation-returning methods stay
    /// and return exactly what they did.
    /// </summary>
    public class Task660RunOutcomeTests
    {
        private sealed class ScriptedProvider(params LlmResponse[] responses) : ILlmProvider
        {
            private readonly Queue<LlmResponse> _responses = new(responses);
            public string Name => "scripted";
            public Action<string, string>? MessageCallback { get; set; }

            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : EndTurn("default"));
            }

            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        private sealed class EchoTool : Tool
        {
            public override string Name => "echo";
            public override string Description => "echo";
            public override object? InputSchema => null;
            public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
                => Task.FromResult("ok");
        }

        private sealed class StopAtFirstIteration : AgentHooks
        {
            public override ValueTask OnIterationStartingAsync(AgentIterationContext context)
            {
                context.Stop("budget");
                return ValueTask.CompletedTask;
            }
        }

        private sealed class TestAgent(ILlmProvider provider, bool verbose = false)
            : Agent(provider, new AgentOptions { Verbose = verbose })
        {
            protected override string SystemPrompt => "test";
            protected override List<Tool> CreateTools() => new() { new EchoTool() };
        }

        private static LlmResponse EndTurn(string text) => new()
        {
            StopReason = "end_turn",
            Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
        };

        private static LlmResponse ToolUse() => new()
        {
            StopReason = "tool_use",
            Content = new List<ContentBlock> { new() { Type = "tool_use", Id = "t1", Name = "echo", Input = new Dictionary<string, object>() } }
        };

        private static LlmResponse Stop(string reason) => new()
        {
            StopReason = reason,
            Content = new List<ContentBlock> { new() { Type = "text", Text = "partial" } }
        };

        [Fact]
        public async Task End_turn_is_completed()
        {
            var result = await new TestAgent(new ScriptedProvider(EndTurn("done"))).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.Completed);
            result.IsCompleted.Should().BeTrue();
            result.StopReason.Should().Be("end_turn");
            result.Failure.Should().BeNull();
        }

        [Fact]
        public async Task An_llm_error_is_failed_with_its_classified_failure()
        {
            var failure = new LlmFailure(LlmFailureKind.RateLimited, 429, TimeSpan.FromSeconds(30));
            var provider = new ScriptedProvider(LlmResponse.Error("slow down", failure));

            var result = await new TestAgent(provider).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.LlmFailed);
            result.Failure.Should().Be(failure);
            result.Failure!.IsTransient.Should().BeTrue();
            result.Detail.Should().Be("slow down");
        }

        [Fact]
        public async Task A_provider_that_is_not_configured_is_not_configured()
        {
            var provider = new ScriptedProvider(new LlmResponse
            {
                StopReason = "NotConfigured",
                ErrorMessage = "no api key",
                Failure = new LlmFailure(LlmFailureKind.NotConfigured),
            });

            var result = await new TestAgent(provider).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.NotConfigured);
            result.Failure!.Kind.Should().Be(LlmFailureKind.NotConfigured);
        }

        [Fact]
        public async Task Running_out_of_iterations_while_calling_tools_is_max_iterations()
        {
            var result = await new TestAgent(new ScriptedProvider(ToolUse(), ToolUse(), ToolUse())).RunWithOutcomeAsync("task", maxIterations: 2);

            result.Outcome.Should().Be(AgentRunOutcome.MaxIterationsReached);
            result.IsCompleted.Should().BeFalse();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task An_unexpected_stop_reason_is_carried_without_changing_the_conversation(bool verbose)
        {
            var conversation = (await new TestAgent(new ScriptedProvider(Stop("content_filter")), verbose).RunAsync("task"));
            var result = await new TestAgent(new ScriptedProvider(Stop("content_filter")), verbose).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.UnexpectedStop);
            result.StopReason.Should().Be("content_filter");
            result.Conversation.Should().BeEquivalentTo(conversation, "the outcome travels on the result, not as injected text");
        }

        [Fact]
        public async Task A_truncated_turn_is_truncated()
        {
            var result = await new TestAgent(new ScriptedProvider(Stop("max_tokens"))).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.Truncated);
        }

        [Fact]
        public async Task A_tool_use_stop_without_a_tool_call_is_missing_tool_call()
        {
            var result = await new TestAgent(new ScriptedProvider(Stop("tool_use"))).RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.MissingToolCall);
        }

        [Fact]
        public async Task A_hook_stop_is_stopped_by_hook_with_its_reason()
        {
            var agent = new TestAgent(new ScriptedProvider(EndTurn("never"))) { Hooks = new StopAtFirstIteration() };

            var result = await agent.RunWithOutcomeAsync("task");

            result.Outcome.Should().Be(AgentRunOutcome.StoppedByHook);
            result.Detail.Should().Be("budget");
            result.StopReason.Should().BeNull("no model turn ended the run");
        }

        [Fact]
        public async Task Continue_and_resume_report_the_outcome_too()
        {
            var history = new List<Message> { new() { Role = "user", Content = "first" }, new() { Role = "assistant", Content = "ok" } };

            (await new TestAgent(new ScriptedProvider(EndTurn("x"))).ContinueWithOutcomeAsync(history, "next")).Outcome
                .Should().Be(AgentRunOutcome.Completed);
            (await new TestAgent(new ScriptedProvider(LlmResponse.Error("boom", new LlmFailure(LlmFailureKind.ServerError, 500))))
                    .ResumeWithOutcomeAsync(new[] { new Message { Role = "user", Content = "q" } })).Outcome
                .Should().Be(AgentRunOutcome.LlmFailed);
        }

        [Fact]
        public async Task The_conversation_methods_return_the_same_conversation_as_before()
        {
            var failure = new LlmFailure(LlmFailureKind.ServerError, 500);
            var viaOld = await new TestAgent(new ScriptedProvider(ToolUse(), LlmResponse.Error("boom", failure))).RunAsync("task");
            var viaNew = await new TestAgent(new ScriptedProvider(ToolUse(), LlmResponse.Error("boom", failure))).RunWithOutcomeAsync("task");

            viaNew.Conversation.Should().BeEquivalentTo(viaOld);
            viaOld[^1].Role.Should().Be("assistant");
            MessageText.From(viaOld[^1].Content).Should().Contain("Error: LLM request failed: boom", "the injected error text is unchanged");
        }

        [Fact]
        public async Task Cancellation_still_throws()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => new TestAgent(new ScriptedProvider(EndTurn("x"))).RunWithOutcomeAsync("task", cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
