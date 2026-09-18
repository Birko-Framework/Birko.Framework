using Birko.AI.Orchestration.Models;
using Birko.AI.Orchestration.Services;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Orchestration.Tests
{
    public class StepDependencyAnalyzerTests
    {
        [Fact]
        public void SuggestOptimalOrder_PlacesCreatorBeforeModifier()
        {
            // Regression for CR-H003: post-order DFS over the depends-on graph already yields a
            // dependencies-first order; the old sorted.Reverse() inverted it, so a step that
            // modifies a file was ordered BEFORE the step that creates it. Assert the creator
            // (A) comes before the modifier (B).
            var creator = new ImplementationStep { Index = 0, Title = "A", FilesToCreate = { "shared.cs" } };
            var modifier = new ImplementationStep { Index = 1, Title = "B", FilesToModify = { "shared.cs" } };

            var analyzer = new StepDependencyAnalyzer();

            // Try both input orderings — the result must be dependency-correct regardless.
            foreach (var input in new[]
            {
                new List<ImplementationStep> { creator, modifier },
                new List<ImplementationStep> { modifier, creator }
            })
            {
                var ordered = analyzer.SuggestOptimalOrder(input);
                ordered.IndexOf(creator).Should().BeLessThan(ordered.IndexOf(modifier),
                    "the file creator must run before the step that modifies that file");
            }
        }

        [Fact]
        public void SuggestOptimalOrder_ChainsTransitiveDependencies()
        {
            // C modifies b.cs (created by B), B modifies a.cs (created by A) => order A, B, C.
            var a = new ImplementationStep { Index = 0, Title = "A", FilesToCreate = { "a.cs" } };
            var b = new ImplementationStep { Index = 1, Title = "B", FilesToCreate = { "b.cs" }, FilesToModify = { "a.cs" } };
            var c = new ImplementationStep { Index = 2, Title = "C", FilesToModify = { "b.cs" } };

            var ordered = new StepDependencyAnalyzer()
                .SuggestOptimalOrder(new List<ImplementationStep> { c, b, a });

            ordered.IndexOf(a).Should().BeLessThan(ordered.IndexOf(b));
            ordered.IndexOf(b).Should().BeLessThan(ordered.IndexOf(c));
        }

        [Fact]
        public void SuggestOptimalOrder_ReturnsAllSteps()
        {
            var steps = new List<ImplementationStep>
            {
                new() { Index = 0, Title = "X" },
                new() { Index = 1, Title = "Y" }
            };

            var ordered = new StepDependencyAnalyzer().SuggestOptimalOrder(steps);

            ordered.Should().HaveCount(2).And.Contain(steps);
        }
    }
}
