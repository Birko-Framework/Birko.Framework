using Birko.AI.Orchestration.Models;

namespace Birko.AI.Orchestration.Validation
{
    /// <summary>
    /// Every file in <see cref="ImplementationStep.FilesToCreate"/> exists.
    /// </summary>
    public class FileCreationValidator : IStepValidator
    {
        public string Name => "FileCreation";

        public Task<StepValidationResult> ValidateAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var issues = step.FilesToCreate
                .Where(f => !File.Exists(StepFiles.FullPath(workingDirectory, f)))
                .Select(f => $"Expected file not created: {f}")
                .ToList();
            return Task.FromResult(StepValidationResult.From(Name, issues));
        }
    }

    /// <summary>
    /// Every file in <see cref="ImplementationStep.FilesToModify"/> exists and, once the step has started, was
    /// written after <see cref="ImplementationStep.StartedAt"/>.
    /// </summary>
    public class FileModificationValidator : IStepValidator
    {
        public string Name => "FileModification";

        public Task<StepValidationResult> ValidateAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var issues = new List<string>();
            foreach (var file in step.FilesToModify)
            {
                var path = StepFiles.FullPath(workingDirectory, file);
                if (!File.Exists(path))
                    issues.Add($"Expected file not found: {file}");
                else if (step.StartedAt.HasValue && File.GetLastWriteTimeUtc(path) < step.StartedAt.Value)
                    issues.Add($"File not modified since step started: {file}");
            }
            return Task.FromResult(StepValidationResult.From(Name, issues));
        }
    }

    /// <summary>
    /// Every string in <see cref="ImplementationStep.ExpectedContent"/> appears (ordinal match) in at least one of the
    /// step's files — so a step that touched a file without doing the work does not pass.
    /// </summary>
    /// <remarks>
    /// Passes when the step declares no expected content. It does <b>not</b> pass a step that declares expected
    /// content but names no files, or whose files cannot be read: those are reported as issues, because "could not
    /// check" is not "checked" (DraCode's original passed both).
    /// </remarks>
    public class ContentExpectationValidator : IStepValidator
    {
        public string Name => "ContentExpectation";

        public async Task<StepValidationResult> ValidateAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var expected = step.ExpectedContent.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (expected.Count == 0) return StepValidationResult.From(Name, new List<string>());

            var files = step.FilesToCreate.Concat(step.FilesToModify).Distinct().ToList();
            if (files.Count == 0)
                return StepValidationResult.From(Name, new List<string>
                {
                    "Expected content is declared but the step names no files to look for it in",
                });

            var issues = new List<string>();
            var contents = new List<string>();
            foreach (var file in files)
            {
                var path = StepFiles.FullPath(workingDirectory, file);
                if (!File.Exists(path)) continue; // reported by the file validators; the content check below still runs
                try
                {
                    contents.Add(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    issues.Add($"Could not read {file}: {ex.Message}");
                }
            }

            var fileList = string.Join(", ", files);
            foreach (var text in expected)
            {
                if (!contents.Any(c => c.Contains(text, StringComparison.Ordinal)))
                    issues.Add($"Expected content not found: '{text}' in [{fileList}]");
            }
            return StepValidationResult.From(Name, issues);
        }
    }

    internal static class StepFiles
    {
        public static string FullPath(string workingDirectory, string file)
            => Path.IsPathRooted(file) ? file : Path.Combine(workingDirectory, file);
    }
}
