namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// Telemetry for one step's execution.
    /// </summary>
    public class StepExecutionMetrics
    {
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Agent iterations spent on the step.
        /// </summary>
        public int IterationsUsed { get; set; }

        /// <summary>
        /// Estimated tokens (input + output) spent on the step.
        /// </summary>
        public int EstimatedTokens { get; set; }

        public bool Failed { get; set; }
        public bool Skipped { get; set; }

        /// <summary>
        /// The orchestrator advanced the step itself (e.g. its validators passed) instead of the agent marking it done.
        /// </summary>
        public bool AutoCompleted { get; set; }

        /// <summary>
        /// Times the step has been through <c>Validation.StepValidationService.ValidateStepAsync</c>.
        /// </summary>
        public int ValidationAttempts { get; set; }

        public double DurationSeconds =>
            StartTime.HasValue && EndTime.HasValue ? (EndTime.Value - StartTime.Value).TotalSeconds : 0;
    }

    /// <summary>
    /// Step metrics summed over a plan — <see cref="ImplementationPlan.GetAggregatedMetrics"/>.
    /// </summary>
    public class PlanExecutionMetrics
    {
        public int TotalSteps { get; set; }
        public int CompletedSteps { get; set; }
        public int FailedSteps { get; set; }
        public int SkippedSteps { get; set; }
        public int TotalIterations { get; set; }
        public long TotalEstimatedTokens { get; set; }
        public double TotalDurationSeconds { get; set; }
        public int AutoCompletedSteps { get; set; }
        public double AverageIterationsPerStep { get; set; }

        /// <summary>
        /// Completed steps as a percentage of all steps (0–100).
        /// </summary>
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// One entry of <see cref="ImplementationPlan.ExecutionLog"/>.
    /// </summary>
    public class PlanLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
