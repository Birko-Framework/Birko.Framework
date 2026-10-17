namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// An implementation plan consisting of ordered steps with file dependencies, plus the execution state a worker
    /// needs to resume it: the current step, per-worker step assignments, an execution log and lessons learned.
    /// </summary>
    /// <remarks>
    /// Not sealed — a consumer that keeps more context per plan (a specification hash, a file name) derives from it.
    /// <see cref="Status"/> and every step's <see cref="ImplementationStep.Status"/> have pinned numeric values; see
    /// <see cref="PlanStatus"/>.
    /// </remarks>
    public class ImplementationPlan
    {
        public string TaskId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string TaskDescription { get; set; } = string.Empty;
        public string? FeatureId { get; set; }

        /// <summary>
        /// Ordered list of implementation steps.
        /// </summary>
        public List<ImplementationStep> Steps { get; set; } = new();

        /// <summary>
        /// Lessons captured during execution for future tasks.
        /// </summary>
        public List<string> LessonsLearned { get; set; } = new();

        /// <summary>
        /// Patterns that worked well during execution.
        /// </summary>
        public List<string> SuccessfulPatterns { get; set; } = new();

        /// <summary>
        /// Issues encountered during execution and resolved by a retry.
        /// </summary>
        public List<string> ResolvedIssues { get; set; } = new();

        /// <summary>
        /// Self-assessments recorded by workers at checkpoints.
        /// </summary>
        public List<ReflectionEntry> Reflections { get; set; } = new();

        /// <summary>
        /// Escalations raised while executing this plan.
        /// </summary>
        public List<EscalationAlert> Escalations { get; set; } = new();

        /// <summary>
        /// Timestamped execution log; append through <see cref="AddLogEntry"/>.
        /// </summary>
        public List<PlanLogEntry> ExecutionLog { get; set; } = new();

        /// <summary>
        /// Step indices (0-based positions in <see cref="Steps"/>) assigned to each worker for parallel execution.
        /// Empty means "not split": every worker runs the whole plan.
        /// </summary>
        public Dictionary<string, List<int>> AssignedStepIndices { get; set; } = new();

        /// <summary>
        /// The position (0-based) in <see cref="Steps"/> of the step to execute next; resumption starts here.
        /// </summary>
        public int CurrentStepIndex { get; set; }

        public PlanStatus Status { get; set; } = PlanStatus.Created;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// The step at <see cref="CurrentStepIndex"/>, or null when every step has been passed.
        /// </summary>
        public ImplementationStep? CurrentStep =>
            CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count ? Steps[CurrentStepIndex] : null;

        /// <summary>
        /// Whether <see cref="CurrentStepIndex"/> still points at a step.
        /// </summary>
        public bool HasMoreSteps => CurrentStepIndex < Steps.Count;

        public int CompletedStepsCount => Steps.Count(s => s.Status == StepStatus.Completed);

        /// <summary>
        /// Completed steps as a percentage of all steps (0–100, rounded down); 0 for a plan with no steps.
        /// </summary>
        public int ProgressPercentage => Steps.Count > 0 ? CompletedStepsCount * 100 / Steps.Count : 0;

        /// <summary>
        /// Moves <see cref="CurrentStepIndex"/> to the next step; does nothing once every step has been passed.
        /// </summary>
        public void AdvanceToNextStep()
        {
            if (CurrentStepIndex >= Steps.Count) return;
            CurrentStepIndex++;
            Touch();
        }

        public void AddLogEntry(string message)
        {
            ExecutionLog.Add(new PlanLogEntry { Timestamp = DateTime.UtcNow, Message = message });
            Touch();
        }

        /// <summary>
        /// Marks the plan as executing.
        /// </summary>
        public void MarkInProgress()
        {
            Status = PlanStatus.InProgress;
            AddLogEntry("Plan execution started");
        }

        /// <summary>
        /// Marks the plan as completed and records lessons learned from its steps (<see cref="CaptureLessonsLearned"/>).
        /// </summary>
        public void MarkCompleted()
        {
            Status = PlanStatus.Completed;
            CompletedAt = DateTime.UtcNow;
            AddLogEntry("Plan completed successfully");
            CaptureLessonsLearned();
        }

        public void MarkFailed(string errorMessage)
        {
            Status = PlanStatus.Failed;
            ErrorMessage = errorMessage;
            CompletedAt = DateTime.UtcNow;
            AddLogEntry($"Plan failed: {errorMessage}");
        }

        /// <summary>
        /// Records what this run can teach a later one: the errors that a retry resolved, steps that took many
        /// iterations, and how many iterations quick steps took. Idempotent — an entry already present is not added
        /// again. Called by <see cref="MarkCompleted"/>; override to change the heuristics.
        /// </summary>
        public virtual void CaptureLessonsLearned()
        {
            foreach (var step in Steps.Where(s => s.Status == StepStatus.Completed && s.RetryCount > 0))
            {
                if (!string.IsNullOrEmpty(step.ErrorMessage))
                    AddOnce(ResolvedIssues, $"Step '{step.Title}' encountered: {step.ErrorMessage}");
                if (step.Metrics.IterationsUsed > 10)
                    AddOnce(LessonsLearned, $"Step '{step.Title}' required {step.Metrics.IterationsUsed} iterations - consider breaking down similar complex steps");
            }

            var quick = Steps.Where(s => s.Status == StepStatus.Completed && s.Metrics.IterationsUsed is > 0 and <= 3).ToList();
            if (quick.Count > 1)
                AddOnce(SuccessfulPatterns, $"Tasks of this type often complete in {quick.Average(s => s.Metrics.IterationsUsed):F1} iterations per step");

            var files = Steps.Sum(s => s.FilesToCreate.Count + s.FilesToModify.Count);
            if (files > 0)
                AddOnce(LessonsLearned, $"This task created/modified {files} files across {Steps.Count} steps");
        }

        /// <summary>
        /// The steps a worker should execute. When the plan has no assignments at all, that is every step. When it
        /// has been split, a worker with no assignment gets <b>none</b> — never the whole plan, which would run every
        /// other worker's steps a second time. Indices outside <see cref="Steps"/> are ignored.
        /// </summary>
        public List<ImplementationStep> GetAssignedSteps(string workerId)
        {
            if (AssignedStepIndices.Count == 0) return Steps.ToList();
            if (!AssignedStepIndices.TryGetValue(workerId, out var indices)) return new List<ImplementationStep>();
            return indices.Where(i => i >= 0 && i < Steps.Count).Select(i => Steps[i]).ToList();
        }

        /// <summary>
        /// Assigns steps (0-based positions in <see cref="Steps"/>) to a worker, replacing its earlier assignment.
        /// </summary>
        public void AssignSteps(string workerId, IEnumerable<int> stepIndices)
        {
            AssignedStepIndices[workerId] = stepIndices.ToList();
            Touch();
        }

        public void ClearStepAssignments(string workerId)
        {
            AssignedStepIndices.Remove(workerId);
            Touch();
        }

        public void ClearAllStepAssignments()
        {
            AssignedStepIndices.Clear();
            Touch();
        }

        /// <summary>
        /// Step counts by outcome, iterations, tokens and duration, summed over the plan.
        /// </summary>
        public PlanExecutionMetrics GetAggregatedMetrics()
        {
            var metrics = new PlanExecutionMetrics
            {
                TotalSteps = Steps.Count,
                CompletedSteps = Steps.Count(s => s.Status == StepStatus.Completed),
                FailedSteps = Steps.Count(s => s.Status == StepStatus.Failed),
                SkippedSteps = Steps.Count(s => s.Status == StepStatus.Skipped),
                TotalIterations = Steps.Sum(s => s.Metrics.IterationsUsed),
                TotalEstimatedTokens = Steps.Sum(s => (long)s.Metrics.EstimatedTokens),
                TotalDurationSeconds = Steps.Sum(s => s.DurationSeconds),
                AutoCompletedSteps = Steps.Count(s => s.Metrics.AutoCompleted),
                AverageIterationsPerStep = Steps.Where(s => s.Metrics.IterationsUsed > 0)
                    .Select(s => (double)s.Metrics.IterationsUsed)
                    .DefaultIfEmpty(0)
                    .Average(),
            };
            if (Steps.Count > 0)
                metrics.SuccessRate = (double)metrics.CompletedSteps / Steps.Count * 100;
            return metrics;
        }

        private void Touch() => UpdatedAt = DateTime.UtcNow;

        private static void AddOnce(List<string> list, string entry)
        {
            if (!list.Contains(entry)) list.Add(entry);
        }
    }

    public class ImplementationStep
    {
        /// <summary>
        /// Step number for display (1-based; <c>StepDependencyAnalyzer.Reorder</c> renumbers it).
        /// </summary>
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StepStatus Status { get; set; } = StepStatus.Pending;

        /// <summary>
        /// Files expected to be created in this step.
        /// </summary>
        public List<string> FilesToCreate { get; set; } = new();

        /// <summary>
        /// Files expected to be modified in this step.
        /// </summary>
        public List<string> FilesToModify { get; set; } = new();

        /// <summary>
        /// Strings (identifiers, signatures) that must appear in this step's files once it is done — checked by
        /// <c>Validation.ContentExpectationValidator</c>, so a step that touched a file without doing the work does
        /// not pass. Example: <c>["GetUserById", "async Task&lt;User&gt;"]</c>.
        /// </summary>
        public List<string> ExpectedContent { get; set; } = new();

        /// <summary>
        /// Result summary of the last completion, or the reason for a skip.
        /// </summary>
        public string? Output { get; set; }

        /// <summary>
        /// The last error, kept across retries — so after a successful retry it still says what went wrong.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Classification of the last error (TASK-531: typed; it was a string each consumer spelled its own way).
        /// </summary>
        public Birko.AI.Models.ErrorCategory? ErrorCategory { get; set; }

        /// <summary>
        /// Retries already scheduled by <see cref="RetryOrFail"/>.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Retries allowed for a retryable error (default 3); 0 makes every failure final.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        public StepExecutionMetrics Metrics { get; set; } = new();
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Whether a retryable failure would be retried rather than final.
        /// </summary>
        public bool CanRetry => RetryCount < MaxRetries;

        /// <summary>
        /// Seconds from start to finish; 0 until both are recorded.
        /// </summary>
        public double DurationSeconds =>
            StartedAt.HasValue && CompletedAt.HasValue ? (CompletedAt.Value - StartedAt.Value).TotalSeconds : 0;

        /// <summary>
        /// Marks the step as executing. Starting again (a retry) clears the previous finish time.
        /// </summary>
        public void Start()
        {
            Status = StepStatus.InProgress;
            StartedAt = DateTime.UtcNow;
            CompletedAt = null;
            Metrics.StartTime = StartedAt;
            Metrics.EndTime = null;
        }

        public void Complete(string? output = null)
        {
            Status = StepStatus.Completed;
            Output = output;
            Finish();
        }

        /// <summary>
        /// Marks the step as finally failed — no retry. For a failure that may be retried use <see cref="RetryOrFail"/>.
        /// </summary>
        public void Fail(string? error = null, Birko.AI.Models.ErrorCategory? errorCategory = null)
        {
            Status = StepStatus.Failed;
            ErrorMessage = error;
            if (errorCategory != null) ErrorCategory = errorCategory;
            Metrics.Failed = true;
            Finish();
        }

        public void Skip(string? reason = null)
        {
            Status = StepStatus.Skipped;
            Output = reason;
            Metrics.Skipped = true;
            Finish();
        }

        /// <summary>
        /// <see cref="RetryOrFail(string?, Birko.AI.Models.ErrorCategory?, bool)"/> for an LLM failure: retryable exactly
        /// when the provider classified it transient (TASK-531), so no caller decides that from the error text.
        /// </summary>
        public bool RetryOrFail(string? error, Birko.AI.Models.LlmFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            return RetryOrFail(error, failure.Category, failure.IsTransient);
        }

        /// <summary>
        /// Records a failure and decides between a retry and a final failure. A retryable error with retries left
        /// puts the step back to <see cref="StepStatus.Pending"/> (start time cleared, <see cref="RetryCount"/> + 1)
        /// and returns <c>true</c>; anything else is <see cref="Fail"/> — with the message saying the retries ran out
        /// when they did — and returns <c>false</c>. The error is kept in <see cref="ErrorMessage"/> either way.
        /// </summary>
        /// <returns><c>true</c> when a retry was scheduled.</returns>
        public bool RetryOrFail(string? error, Birko.AI.Models.ErrorCategory? errorCategory, bool retryable)
        {
            ErrorMessage = error;
            ErrorCategory = errorCategory;
            if (retryable && CanRetry)
            {
                RetryCount++;
                Status = StepStatus.Pending;
                StartedAt = null;
                CompletedAt = null;
                return true;
            }
            Fail(retryable ? $"Max retries ({MaxRetries}) exhausted. Last error: {error}" : error, errorCategory);
            return false;
        }

        private void Finish()
        {
            CompletedAt = DateTime.UtcNow;
            Metrics.EndTime = CompletedAt;
        }
    }

    /// <summary>
    /// Status of an implementation plan. ⚠ The numeric values are <b>pinned</b> — a plan stored as an integer
    /// (a database column, JSON without a string-enum converter) reads back as the same status, and they match
    /// DraCode.KoboldLair's ordering so its stored plans need no remapping (DraCode's <c>Planning</c> is
    /// <see cref="Created"/>). Never renumber; add new members with new values.
    /// </summary>
    public enum PlanStatus
    {
        /// <summary>Being generated.</summary>
        Created = 0,
        /// <summary>Generated and ready to execute.</summary>
        Ready = 1,
        InProgress = 2,
        Completed = 3,
        Failed = 4,
    }

    /// <summary>
    /// Status of an implementation step. ⚠ The numeric values are <b>pinned</b> (see <see cref="PlanStatus"/>):
    /// <see cref="Skipped"/> is 3 and <see cref="Failed"/> is 4, DraCode.KoboldLair's ordering. Never renumber.
    /// </summary>
    public enum StepStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Skipped = 3,
        Failed = 4,
    }
}
