using Birko.AI.Orchestration.Models;

namespace Birko.AI.Orchestration.Services
{
    /// <summary>
    /// Analyzes step dependencies to enable parallel execution.
    /// Groups steps that can run concurrently based on file dependencies.
    /// </summary>
    public class StepDependencyAnalyzer
    {
        /// <summary>
        /// Returns groups of steps that can execute in parallel.
        /// Each group contains steps with no file dependencies on each other.
        /// </summary>
        public List<List<ImplementationStep>> AnalyzeParallelGroups(ImplementationPlan plan)
        {
            var groups = new List<List<ImplementationStep>>();
            var remainingSteps = new List<ImplementationStep>(plan.Steps);

            while (remainingSteps.Count > 0)
            {
                var currentGroup = new List<ImplementationStep>();
                var filesTouched = new HashSet<string>();

                foreach (var step in remainingSteps.ToList())
                {
                    var dependsOnGroupFiles = step.FilesToModify.Any(f => filesTouched.Contains(f));
                    var createsConflictingFile = step.FilesToCreate.Any(f => filesTouched.Contains(f));

                    if (!dependsOnGroupFiles && !createsConflictingFile)
                    {
                        currentGroup.Add(step);
                        remainingSteps.Remove(step);

                        foreach (var file in step.FilesToCreate.Concat(step.FilesToModify))
                            filesTouched.Add(file);
                    }
                }

                if (currentGroup.Count > 0)
                {
                    groups.Add(currentGroup);
                }
                else if (remainingSteps.Count > 0)
                {
                    // Deadlock — force first remaining step into its own group
                    groups.Add(new List<ImplementationStep> { remainingSteps[0] });
                    remainingSteps.RemoveAt(0);
                }
            }

            return groups;
        }

        /// <summary>
        /// Checks if two steps have file dependencies preventing parallel execution.
        /// </summary>
        public bool HasDependency(ImplementationStep step1, ImplementationStep step2)
        {
            if (step2.FilesToModify.Any(f => step1.FilesToCreate.Contains(f)))
                return true;
            if (step2.FilesToCreate.Any(f => step1.FilesToModify.Contains(f)))
                return true;
            if (step1.FilesToModify.Any(f => step2.FilesToModify.Contains(f)))
                return true;
            if (step1.FilesToCreate.Any(f => step2.FilesToCreate.Contains(f)))
                return true;
            return false;
        }

        /// <summary>
        /// Topological sort for optimal execution order based on file dependencies.
        /// </summary>
        public List<ImplementationStep> SuggestOptimalOrder(List<ImplementationStep> steps)
        {
            var dependencies = new Dictionary<ImplementationStep, List<ImplementationStep>>();
            foreach (var step in steps)
            {
                dependencies[step] = new List<ImplementationStep>();
                foreach (var other in steps)
                {
                    if (step != other && step.FilesToModify.Any(f => other.FilesToCreate.Contains(f)))
                        dependencies[step].Add(other);
                }
            }

            var sorted = new List<ImplementationStep>();
            var visited = new HashSet<ImplementationStep>();
            var visiting = new HashSet<ImplementationStep>();

            foreach (var step in steps)
            {
                if (!visited.Contains(step))
                    Visit(step, dependencies, visited, visiting, sorted);
            }

            // Post-order DFS over a depends-on graph (edges point from a step to the steps it
            // depends on) already yields a valid dependencies-first execution order — a step's
            // dependencies are appended before the step itself. No reversal (CR-H003: the old
            // sorted.Reverse() inverted this into an invalid dependents-first order).
            return sorted;
        }

        /// <summary>
        /// The ordering violations in a plan: a step that modifies a file a <b>later</b> step creates. Empty when the
        /// order is valid. Steps that merely touch the same file, or a creator placed before its modifier, are not
        /// violations — only "used before it exists" is.
        /// </summary>
        public List<string> ValidateOrdering(ImplementationPlan plan)
        {
            var violations = new List<string>();
            for (var i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                for (var j = i + 1; j < plan.Steps.Count; j++)
                {
                    var later = plan.Steps[j];
                    var files = step.FilesToModify.Where(f => later.FilesToCreate.Contains(f)).ToList();
                    if (files.Count == 0) continue;
                    violations.Add($"Step {step.Index} ('{step.Title}') modifies {string.Join(", ", files)}, " +
                                   $"which step {later.Index} ('{later.Title}') creates later");
                }
            }
            return violations;
        }

        /// <summary>
        /// Puts the plan's steps into <see cref="SuggestOptimalOrder"/>'s order and renumbers
        /// <see cref="ImplementationStep.Index"/> 1..n. Returns false, changing nothing, when the order is already
        /// that one.
        /// </summary>
        /// <exception cref="InvalidOperationException">The plan has started (a step is not pending, or
        /// <see cref="ImplementationPlan.CurrentStepIndex"/> has moved) or has step assignments — both are positions
        /// in <see cref="ImplementationPlan.Steps"/>, and reordering would silently point them at other steps.</exception>
        public bool Reorder(ImplementationPlan plan)
        {
            if (plan.CurrentStepIndex != 0 || plan.Steps.Any(s => s.Status != StepStatus.Pending))
                throw new InvalidOperationException("Cannot reorder a plan that has started executing: CurrentStepIndex and step positions would point at different steps.");
            if (plan.AssignedStepIndices.Count > 0)
                throw new InvalidOperationException("Cannot reorder a plan with step assignments: they are positions in Steps. Clear them first, then assign again.");

            var ordered = SuggestOptimalOrder(plan.Steps);
            if (ordered.SequenceEqual(plan.Steps)) return false;

            plan.Steps.Clear();
            plan.Steps.AddRange(ordered);
            for (var i = 0; i < plan.Steps.Count; i++)
                plan.Steps[i].Index = i + 1;
            plan.AddLogEntry("Steps reordered based on file dependencies: " +
                             string.Join(", ", plan.Steps.Select(s => $"{s.Index}. {s.Title}")));
            return true;
        }

        private void Visit(
            ImplementationStep step,
            Dictionary<ImplementationStep, List<ImplementationStep>> dependencies,
            HashSet<ImplementationStep> visited,
            HashSet<ImplementationStep> visiting,
            List<ImplementationStep> sorted)
        {
            if (visiting.Contains(step)) return; // circular — skip
            if (visited.Contains(step)) return;

            visiting.Add(step);
            foreach (var dep in dependencies[step])
                Visit(dep, dependencies, visited, visiting, sorted);

            visiting.Remove(step);
            visited.Add(step);
            sorted.Add(step);
        }
    }
}
