using Birko.AI.Orchestration.Models;
using Birko.AI.Orchestration.Services;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Orchestration.Tests
{
    /// <summary>
    /// TASK-528 — <see cref="StepDependencyAnalyzer.ValidateOrdering"/> and <see cref="StepDependencyAnalyzer.Reorder"/>.
    /// </summary>
    public class StepOrderingTests
    {
        private static ImplementationStep Creates(int index, string file) => new() { Index = index, Title = $"S{index}", FilesToCreate = { file } };
        private static ImplementationStep Modifies(int index, string file) => new() { Index = index, Title = $"S{index}", FilesToModify = { file } };

        private readonly StepDependencyAnalyzer _analyzer = new();

        [Fact]
        public void A_creator_before_its_modifier_is_valid()
        {
            // DraCode's version reported this as a violation: its check was the symmetric HasDependency.
            var plan = new ImplementationPlan { Steps = { Creates(1, "a.cs"), Modifies(2, "a.cs") } };
            _analyzer.ValidateOrdering(plan).Should().BeEmpty();
        }

        [Fact]
        public void Steps_that_touch_the_same_file_without_creating_it_are_valid()
        {
            var plan = new ImplementationPlan { Steps = { Modifies(1, "a.cs"), Modifies(2, "a.cs") } };
            _analyzer.ValidateOrdering(plan).Should().BeEmpty();
        }

        [Fact]
        public void A_modifier_before_its_creator_is_a_violation_naming_the_file()
        {
            var plan = new ImplementationPlan { Steps = { Modifies(1, "a.cs"), Creates(2, "a.cs") } };

            _analyzer.ValidateOrdering(plan).Should().ContainSingle()
                .Which.Should().Be("Step 1 ('S1') modifies a.cs, which step 2 ('S2') creates later");
        }

        [Fact]
        public void Reorder_fixes_the_violation_and_renumbers()
        {
            var plan = new ImplementationPlan { Steps = { Modifies(1, "a.cs"), Creates(2, "a.cs"), Modifies(3, "b.cs") } };

            _analyzer.Reorder(plan).Should().BeTrue();

            plan.Steps.Select(s => s.Title).Should().Equal("S2", "S1", "S3");
            plan.Steps.Select(s => s.Index).Should().Equal(1, 2, 3);
            _analyzer.ValidateOrdering(plan).Should().BeEmpty();
            plan.ExecutionLog.Should().ContainSingle(e => e.Message.StartsWith("Steps reordered"));
        }

        [Fact]
        public void Reorder_of_a_valid_order_changes_nothing()
        {
            var plan = new ImplementationPlan { Steps = { Creates(1, "a.cs"), Modifies(2, "a.cs") } };

            _analyzer.Reorder(plan).Should().BeFalse();

            plan.Steps.Select(s => s.Title).Should().Equal("S1", "S2");
            plan.ExecutionLog.Should().BeEmpty();
        }

        [Fact]
        public void Reorder_refuses_a_started_plan()
        {
            var plan = new ImplementationPlan { Steps = { Modifies(1, "a.cs"), Creates(2, "a.cs") } };
            plan.Steps[0].Start();

            var act = () => _analyzer.Reorder(plan);

            act.Should().Throw<InvalidOperationException>().WithMessage("*started*");
            plan.Steps.Select(s => s.Title).Should().Equal("S1", "S2");
        }

        [Fact]
        public void Reorder_refuses_a_plan_with_assignments()
        {
            var plan = new ImplementationPlan { Steps = { Modifies(1, "a.cs"), Creates(2, "a.cs") } };
            plan.AssignSteps("w1", new[] { 0 });

            var act = () => _analyzer.Reorder(plan);

            act.Should().Throw<InvalidOperationException>().WithMessage("*assignments*");
        }
    }
}
