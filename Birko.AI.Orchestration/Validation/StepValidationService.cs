using Birko.AI.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Orchestration.Validation
{
    /// <summary>
    /// Runs a set of <see cref="IStepValidator"/>s over a step. By default: <see cref="FileCreationValidator"/>,
    /// <see cref="FileModificationValidator"/> and <see cref="ContentExpectationValidator"/>.
    /// </summary>
    public class StepValidationService
    {
        private readonly List<IStepValidator> _validators;
        private readonly ILogger? _logger;

        /// <summary>
        /// The default validators.
        /// </summary>
        public StepValidationService(ILogger? logger = null)
            : this(new IStepValidator[] { new FileCreationValidator(), new FileModificationValidator(), new ContentExpectationValidator() }, logger)
        {
        }

        /// <summary>
        /// Exactly the given validators.
        /// </summary>
        public StepValidationService(IEnumerable<IStepValidator> validators, ILogger? logger = null)
        {
            _validators = (validators ?? throw new ArgumentNullException(nameof(validators))).ToList();
            _logger = logger;
        }

        public IReadOnlyList<IStepValidator> Validators => _validators;

        public void AddValidator(IStepValidator validator)
            => _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));

        /// <returns>Whether a validator with that name was removed.</returns>
        public bool RemoveValidator(string validatorName) => _validators.RemoveAll(v => v.Name == validatorName) > 0;

        /// <summary>
        /// Runs every validator and combines the verdicts; counts one <see cref="StepExecutionMetrics.ValidationAttempts"/>.
        /// A validator that throws is a failed check (its issue says so), never a passed one; cancellation propagates.
        /// </summary>
        public async Task<CombinedStepValidationResult> ValidateStepAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            step.Metrics.ValidationAttempts++;

            var combined = new CombinedStepValidationResult();
            foreach (var validator in _validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await validator.ValidateAsync(step, workingDirectory, cancellationToken).ConfigureAwait(false);
                    combined.IndividualResults.Add(result);
                    if (!result.Success)
                        combined.AllIssues.AddRange(result.Issues.Select(i => $"[{validator.Name}] {i}"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogWarning(ex, "Validator {ValidatorName} failed for step {StepIndex}", validator.Name, step.Index);
                    combined.AllIssues.Add($"[{validator.Name}] Validation error: {ex.Message}");
                }
            }
            combined.Success = combined.AllIssues.Count == 0;
            return combined;
        }
    }
}
