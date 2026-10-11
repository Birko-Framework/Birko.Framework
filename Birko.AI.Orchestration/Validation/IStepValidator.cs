using Birko.AI.Orchestration.Models;

namespace Birko.AI.Orchestration.Validation
{
    /// <summary>
    /// Checks whether a step's work was actually done — run before a step is treated as complete.
    /// </summary>
    public interface IStepValidator
    {
        string Name { get; }

        /// <summary>
        /// Validates the step against the files under <paramref name="workingDirectory"/> (step paths are relative
        /// to it unless rooted).
        /// </summary>
        Task<StepValidationResult> ValidateAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One validator's verdict on a step. Named apart from <c>Birko.Validation.ValidationResult</c> on purpose, so a
    /// file importing both namespaces needs no alias.
    /// </summary>
    public class StepValidationResult
    {
        public string ValidatorName { get; set; } = string.Empty;
        public bool Success { get; set; }

        /// <summary>
        /// What failed; empty when <see cref="Success"/>.
        /// </summary>
        public List<string> Issues { get; set; } = new();

        internal static StepValidationResult From(string validatorName, List<string> issues)
            => new() { ValidatorName = validatorName, Success = issues.Count == 0, Issues = issues };
    }

    /// <summary>
    /// Every validator's verdict on a step — <see cref="StepValidationService.ValidateStepAsync"/>.
    /// </summary>
    public class CombinedStepValidationResult
    {
        public bool Success { get; set; }

        /// <summary>
        /// Every issue, each prefixed with its validator's name: <c>[FileCreation] Expected file not created: a.cs</c>.
        /// </summary>
        public List<string> AllIssues { get; set; } = new();
        public List<StepValidationResult> IndividualResults { get; set; } = new();
    }
}
