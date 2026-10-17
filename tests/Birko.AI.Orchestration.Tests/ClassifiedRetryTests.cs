using Birko.AI.Models;
using Birko.AI.Orchestration.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Orchestration.Tests
{
    /// <summary>
    /// TASK-531 — a step's retry decision taken from the provider's classification, never from the error text.
    /// </summary>
    public class ClassifiedRetryTests
    {
        [Fact]
        public void A_transient_failure_is_retried_and_recorded_as_transient()
        {
            var step = new ImplementationStep();
            step.Start();

            step.RetryOrFail("503", LlmFailure.FromStatusCode(503)).Should().BeTrue();

            step.Status.Should().Be(StepStatus.Pending);
            step.ErrorCategory.Should().Be(ErrorCategory.Transient);
        }

        [Fact]
        public void A_permanent_failure_fails_at_once_and_is_recorded_as_permanent()
        {
            var step = new ImplementationStep();
            step.Start();

            step.RetryOrFail("quota", new LlmFailure(LlmFailureKind.Billing, 429)).Should().BeFalse();

            step.Status.Should().Be(StepStatus.Failed);
            step.RetryCount.Should().Be(0);
            step.ErrorCategory.Should().Be(ErrorCategory.Permanent);
        }

        [Fact]
        public void A_task_record_carries_a_typed_category()
        {
            var record = new AgentTaskRecord { ErrorCategory = new LlmFailure(LlmFailureKind.Network).Category };
            record.ErrorCategory.Should().Be(ErrorCategory.Transient);
        }
    }
}
