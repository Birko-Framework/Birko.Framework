using Birko.AI;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    public class AgentOptionsTests
    {
        [Fact]
        public void Clone_PreservesOnLlmResponseReceived()
        {
            // Regression for CR-H002: Clone() copied every field except OnLlmResponseReceived,
            // so a cloned options instance silently lost the post-response hook.
            var invoked = 0;
            var options = new AgentOptions
            {
                MaxIterations = 42,
                OnLlmResponseReceived = () => invoked++
            };

            var clone = options.Clone();
            clone.OnLlmResponseReceived.Should().NotBeNull();
            clone.OnLlmResponseReceived!.Invoke();

            invoked.Should().Be(1);
            clone.MaxIterations.Should().Be(42);
        }

        [Fact]
        public void Merge_CopiesOnLlmResponseReceived_WhenPresent()
        {
            var invoked = 0;
            var target = new AgentOptions();
            var source = new AgentOptions { OnLlmResponseReceived = () => invoked++ };

            target.Merge(source);
            target.OnLlmResponseReceived.Should().NotBeNull();
            target.OnLlmResponseReceived!.Invoke();

            invoked.Should().Be(1);
        }

        [Fact]
        public void Merge_DoesNotClobberCallback_WhenSourceNull()
        {
            var invoked = 0;
            var target = new AgentOptions { OnLlmResponseReceived = () => invoked++ };

            target.Merge(new AgentOptions { OnLlmResponseReceived = null });
            target.OnLlmResponseReceived.Should().NotBeNull();
            target.OnLlmResponseReceived!.Invoke();

            invoked.Should().Be(1);
        }

        [Fact]
        public void DictionaryRoundTrip_PreservesAllPersistedFields()
        {
            // Regression for CR-M007: ToDictionary/FromDictionary silently dropped
            // AllowedExternalPaths, EnableStreaming, StreamingFallbackToSync and CheckpointInterval,
            // so a serialize/rebuild round-trip reset those four fields to their defaults.
            var original = new AgentOptions
            {
                Interactive = false,
                MaxIterations = 7,
                MaxIterationsPerStep = 4,
                Verbose = false,
                WorkingDirectory = "/work",
                PromptTimeout = 120,
                DefaultPromptResponse = "yes",
                ModelDepth = 8,
                AllowedExternalPaths = new List<string> { "/etc/hosts", "/opt/data" },
                EnableStreaming = true,
                StreamingFallbackToSync = false,
                CheckpointInterval = 5
            };

            var restored = AgentOptions.FromDictionary(original.ToDictionary());

            restored.Interactive.Should().BeFalse();
            restored.MaxIterations.Should().Be(7);
            restored.MaxIterationsPerStep.Should().Be(4);
            restored.Verbose.Should().BeFalse();
            restored.WorkingDirectory.Should().Be("/work");
            restored.PromptTimeout.Should().Be(120);
            restored.DefaultPromptResponse.Should().Be("yes");
            restored.ModelDepth.Should().Be(8);
            restored.AllowedExternalPaths.Should().Equal("/etc/hosts", "/opt/data");
            restored.EnableStreaming.Should().BeTrue();
            restored.StreamingFallbackToSync.Should().BeFalse();
            restored.CheckpointInterval.Should().Be(5);
        }

        [Fact]
        public void DictionaryRoundTrip_EmptyAllowedExternalPaths_StaysEmpty()
        {
            var restored = AgentOptions.FromDictionary(new AgentOptions().ToDictionary());
            restored.AllowedExternalPaths.Should().BeEmpty();
        }
    }
}
