using System.Text.Json;
using Birko.AI.Orchestration.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Orchestration.Tests
{
    /// <summary>
    /// TASK-528 — the plan / step model's behaviour (backported from DraCode.KoboldLair's KoboldImplementationPlan)
    /// and the pinned enum encoding.
    /// </summary>
    public class ImplementationPlanTests
    {
        private static ImplementationPlan Plan(int steps) => new()
        {
            Steps = Enumerable.Range(1, steps).Select(i => new ImplementationStep { Index = i, Title = $"S{i}" }).ToList(),
        };

        // ── step lifecycle ──

        [Fact]
        public void Start_then_Complete_records_status_times_output_and_metrics()
        {
            var step = new ImplementationStep();

            step.Start();
            step.Status.Should().Be(StepStatus.InProgress);
            step.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            step.DurationSeconds.Should().Be(0, "not finished yet");

            step.Complete("done it");
            step.Status.Should().Be(StepStatus.Completed);
            step.Output.Should().Be("done it");
            step.CompletedAt.Should().NotBeNull().And.BeOnOrAfter(step.StartedAt!.Value);
            step.Metrics.StartTime.Should().Be(step.StartedAt);
            step.Metrics.EndTime.Should().Be(step.CompletedAt);
            step.DurationSeconds.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void Fail_and_Skip_are_terminal_and_flag_the_metrics()
        {
            var failed = new ImplementationStep();
            failed.Start();
            failed.Fail("boom", "Permanent");
            failed.Status.Should().Be(StepStatus.Failed);
            failed.ErrorMessage.Should().Be("boom");
            failed.ErrorCategory.Should().Be("Permanent");
            failed.Metrics.Failed.Should().BeTrue();
            failed.CompletedAt.Should().NotBeNull();

            var skipped = new ImplementationStep();
            skipped.Skip("not needed");
            skipped.Status.Should().Be(StepStatus.Skipped);
            skipped.Output.Should().Be("not needed");
            skipped.Metrics.Skipped.Should().BeTrue();
        }

        [Fact]
        public void Starting_again_clears_the_previous_finish()
        {
            var step = new ImplementationStep();
            step.Start();
            step.Complete();

            step.Start();

            step.CompletedAt.Should().BeNull();
            step.Metrics.EndTime.Should().BeNull();
            step.Status.Should().Be(StepStatus.InProgress);
        }

        // ── retry bookkeeping ──

        [Fact]
        public void A_retryable_failure_with_retries_left_goes_back_to_pending()
        {
            var step = new ImplementationStep { MaxRetries = 2 };
            step.Start();

            step.RetryOrFail("timeout", "Transient", retryable: true).Should().BeTrue();

            step.Status.Should().Be(StepStatus.Pending);
            step.RetryCount.Should().Be(1);
            step.StartedAt.Should().BeNull();
            step.ErrorMessage.Should().Be("timeout");
            step.ErrorCategory.Should().Be("Transient");
            step.Metrics.Failed.Should().BeFalse("a retry is not a failure");
        }

        [Fact]
        public void Retries_run_out_at_MaxRetries_and_the_failure_says_so()
        {
            var step = new ImplementationStep { MaxRetries = 2 };

            step.RetryOrFail("t1", "Transient", true).Should().BeTrue();
            step.RetryOrFail("t2", "Transient", true).Should().BeTrue();
            step.CanRetry.Should().BeFalse();
            step.RetryOrFail("t3", "Transient", true).Should().BeFalse();

            step.Status.Should().Be(StepStatus.Failed);
            step.RetryCount.Should().Be(2);
            step.ErrorMessage.Should().Be("Max retries (2) exhausted. Last error: t3");
        }

        [Fact]
        public void A_non_retryable_failure_is_final_on_the_first_attempt()
        {
            var step = new ImplementationStep();

            step.RetryOrFail("syntax error", "Permanent", retryable: false).Should().BeFalse();

            step.Status.Should().Be(StepStatus.Failed);
            step.RetryCount.Should().Be(0);
            step.ErrorMessage.Should().Be("syntax error");
        }

        [Fact]
        public void MaxRetries_0_makes_a_retryable_failure_final()
        {
            var step = new ImplementationStep { MaxRetries = 0 };
            step.RetryOrFail("t", "Transient", true).Should().BeFalse();
            step.Status.Should().Be(StepStatus.Failed);
        }

        // ── navigation ──

        [Fact]
        public void Current_step_advances_and_stops_at_the_end()
        {
            var plan = Plan(2);
            plan.CurrentStep!.Title.Should().Be("S1");

            plan.AdvanceToNextStep();
            plan.CurrentStep!.Title.Should().Be("S2");
            plan.HasMoreSteps.Should().BeTrue();

            plan.AdvanceToNextStep();
            plan.CurrentStep.Should().BeNull();
            plan.HasMoreSteps.Should().BeFalse();

            plan.AdvanceToNextStep();
            plan.CurrentStepIndex.Should().Be(2, "advancing past the end does nothing");
        }

        [Fact]
        public void Progress_counts_completed_steps()
        {
            var plan = Plan(3);
            plan.ProgressPercentage.Should().Be(0);
            plan.Steps[0].Complete();
            plan.Steps[1].Skip();
            plan.CompletedStepsCount.Should().Be(1);
            plan.ProgressPercentage.Should().Be(33);
            new ImplementationPlan().ProgressPercentage.Should().Be(0);
        }

        [Fact]
        public void Plan_status_transitions_log_and_stamp()
        {
            var plan = Plan(1);
            plan.MarkInProgress();
            plan.Status.Should().Be(PlanStatus.InProgress);

            plan.MarkFailed("gave up");
            plan.Status.Should().Be(PlanStatus.Failed);
            plan.ErrorMessage.Should().Be("gave up");
            plan.CompletedAt.Should().NotBeNull();
            plan.ExecutionLog.Select(e => e.Message).Should().Equal("Plan execution started", "Plan failed: gave up");
        }

        // ── per-worker assignment ──

        [Fact]
        public void An_unsplit_plan_gives_every_worker_every_step()
        {
            var plan = Plan(3);
            plan.GetAssignedSteps("w1").Select(s => s.Title).Should().Equal("S1", "S2", "S3");
        }

        [Fact]
        public void A_split_plan_gives_each_worker_its_own_steps_and_an_unassigned_worker_none()
        {
            var plan = Plan(4);
            plan.AssignSteps("w1", new[] { 0, 2, 9 });
            plan.AssignSteps("w2", new[] { 1, 3 });

            plan.GetAssignedSteps("w1").Select(s => s.Title).Should().Equal(new[] { "S1", "S3" }, "index 9 is out of range");
            plan.GetAssignedSteps("w2").Select(s => s.Title).Should().Equal("S2", "S4");
            plan.GetAssignedSteps("w3").Should().BeEmpty(
                "a worker missing from a split plan must not run every other worker's steps a second time");

            plan.ClearStepAssignments("w1");
            plan.AssignedStepIndices.Keys.Should().Equal("w2");
            plan.ClearAllStepAssignments();
            plan.GetAssignedSteps("w3").Should().HaveCount(4);
        }

        // ── metrics / lessons ──

        [Fact]
        public void Aggregated_metrics_sum_the_steps()
        {
            var plan = Plan(4);
            plan.Steps[0].Metrics.IterationsUsed = 2;
            plan.Steps[0].Metrics.EstimatedTokens = int.MaxValue;
            plan.Steps[0].Complete();
            plan.Steps[1].Metrics.IterationsUsed = 4;
            plan.Steps[1].Metrics.EstimatedTokens = int.MaxValue;
            plan.Steps[1].Metrics.AutoCompleted = true;
            plan.Steps[1].Complete();
            plan.Steps[2].Fail("x");
            plan.Steps[3].Skip();

            var m = plan.GetAggregatedMetrics();

            m.TotalSteps.Should().Be(4);
            m.CompletedSteps.Should().Be(2);
            m.FailedSteps.Should().Be(1);
            m.SkippedSteps.Should().Be(1);
            m.TotalIterations.Should().Be(6);
            m.TotalEstimatedTokens.Should().Be(2L * int.MaxValue, "summed as long, no overflow");
            m.AutoCompletedSteps.Should().Be(1);
            m.AverageIterationsPerStep.Should().Be(3);
            m.SuccessRate.Should().Be(50);
        }

        [Fact]
        public void Completing_the_plan_captures_lessons_once()
        {
            var plan = Plan(2);
            plan.Steps[0].FilesToCreate.Add("a.cs");
            plan.Steps[0].RetryOrFail("rate limited", "Transient", true);
            plan.Steps[0].Metrics.IterationsUsed = 12;
            plan.Steps[0].Complete();
            plan.Steps[1].Complete();

            plan.MarkCompleted();
            plan.CaptureLessonsLearned();

            plan.Status.Should().Be(PlanStatus.Completed);
            plan.ResolvedIssues.Should().Equal("Step 'S1' encountered: rate limited");
            plan.LessonsLearned.Should().HaveCount(2).And.Contain(l => l.Contains("12 iterations")).And.Contain(l => l.Contains("1 files"));
        }

        // ── pinned encoding ──

        [Fact]
        public void Enum_values_are_pinned()
        {
            new[] { (int)PlanStatus.Created, (int)PlanStatus.Ready, (int)PlanStatus.InProgress, (int)PlanStatus.Completed, (int)PlanStatus.Failed }
                .Should().Equal(0, 1, 2, 3, 4);
            new[] { (int)StepStatus.Pending, (int)StepStatus.InProgress, (int)StepStatus.Completed, (int)StepStatus.Skipped, (int)StepStatus.Failed }
                .Should().Equal(0, 1, 2, 3, 4);
            new[] { (int)AgentTaskStatus.Unassigned, (int)AgentTaskStatus.NotInitialized, (int)AgentTaskStatus.Working,
                    (int)AgentTaskStatus.Done, (int)AgentTaskStatus.Failed, (int)AgentTaskStatus.BlockedByFailure }
                .Should().Equal(0, 1, 2, 3, 4, 5);
            ((int)EscalationStatus.Failed).Should().Be(3);
            ((int)EscalationType.WrongAgentType).Should().Be(4);
            ((int)ReflectionDecision.Escalate).Should().Be(2);
            ((int)EscalationSource.ReasoningMonitor).Should().Be(1);
        }

        [Fact]
        public void A_plan_DraCode_stored_as_integers_reads_back_with_the_same_statuses()
        {
            // DraCode.KoboldLair serializes without a string-enum converter: Skipped = 3, Failed = 4; plan Ready = 1.
            const string stored = "{\"Status\":1,\"Steps\":[{\"Status\":3},{\"Status\":4},{\"Status\":2}]}";

            var plan = JsonSerializer.Deserialize<ImplementationPlan>(stored)!;

            plan.Status.Should().Be(PlanStatus.Ready);
            plan.Steps.Select(s => s.Status).Should().Equal(StepStatus.Skipped, StepStatus.Failed, StepStatus.Completed);
            JsonSerializer.Serialize(plan).Should().Contain("\"Status\":1,").And.Contain("\"Status\":4,");
        }

        [Fact]
        public void An_escalation_carries_the_worker_that_raised_it()
        {
            var alert = new EscalationAlert { WorkerId = "w2" };
            var back = JsonSerializer.Deserialize<EscalationAlert>(JsonSerializer.Serialize(alert))!;
            back.WorkerId.Should().Be("w2");
        }
    }
}
