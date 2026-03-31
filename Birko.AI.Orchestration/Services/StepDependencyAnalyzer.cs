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

            sorted.Reverse();
            return sorted;
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
