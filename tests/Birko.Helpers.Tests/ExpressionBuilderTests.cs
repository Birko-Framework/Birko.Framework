using System;
using System.Linq.Expressions;
using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests
{
    public class ExpressionBuilderTests
    {
        private class TestModel
        {
            public string? Name { get; set; }
            public int Age { get; set; }
            public bool IsActive { get; set; }
        }

        [Fact]
        public void Build_Empty_ReturnsNull()
        {
            var result = new ExpressionBuilder<TestModel>().Build();
            result.Should().BeNull();
        }

        [Fact]
        public void Build_SingleAnd_ReturnsExpression()
        {
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .Build();

            result.Should().NotBeNull();
            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true }).Should().BeTrue();
            compiled(new TestModel { IsActive = false }).Should().BeFalse();
        }

        [Fact]
        public void And_MultipleConditions_CombinesWithAndAlso()
        {
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .And(x => x.Age >= 18)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true, Age = 20 }).Should().BeTrue();
            compiled(new TestModel { IsActive = true, Age = 10 }).Should().BeFalse();
            compiled(new TestModel { IsActive = false, Age = 20 }).Should().BeFalse();
        }

        [Fact]
        public void Or_MultipleConditions_CombinesWithOrElse()
        {
            var result = new ExpressionBuilder<TestModel>()
                .Or(x => x.IsActive)
                .Or(x => x.Age >= 18)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true, Age = 10 }).Should().BeTrue();
            compiled(new TestModel { IsActive = false, Age = 20 }).Should().BeTrue();
            compiled(new TestModel { IsActive = false, Age = 10 }).Should().BeFalse();
        }

        [Fact]
        public void AndIf_ConditionTrue_AddsExpression()
        {
            string? search = "John";

            var result = new ExpressionBuilder<TestModel>()
                .AndIf(!string.IsNullOrEmpty(search), x => x.Name!.Contains(search!))
                .Build();

            result.Should().NotBeNull();
            var compiled = result!.Compile();
            compiled(new TestModel { Name = "John Doe" }).Should().BeTrue();
            compiled(new TestModel { Name = "Jane Doe" }).Should().BeFalse();
        }

        [Fact]
        public void AndIf_ConditionFalse_SkipsExpression()
        {
            string? search = null;

            var result = new ExpressionBuilder<TestModel>()
                .AndIf(!string.IsNullOrEmpty(search), x => x.Name!.Contains(search!))
                .Build();

            result.Should().BeNull();
        }

        [Fact]
        public void OrIf_ConditionTrue_AddsExpression()
        {
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .OrIf(true, x => x.Age >= 65)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = false, Age = 70 }).Should().BeTrue();
        }

        [Fact]
        public void OrIf_ConditionFalse_SkipsExpression()
        {
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .OrIf(false, x => x.Age >= 65)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = false, Age = 70 }).Should().BeFalse();
        }

        [Fact]
        public void Not_NegatesExpression()
        {
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .Not()
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true }).Should().BeFalse();
            compiled(new TestModel { IsActive = false }).Should().BeTrue();
        }

        [Fact]
        public void Not_EmptyBuilder_Throws()
        {
            var builder = new ExpressionBuilder<TestModel>();
            var act = () => builder.Not();
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void And_NullExpression_Throws()
        {
            var builder = new ExpressionBuilder<TestModel>();
            var act = () => builder.And(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Or_NullExpression_Throws()
        {
            var builder = new ExpressionBuilder<TestModel>();
            var act = () => builder.Or(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithExpression_SetsInitial()
        {
            Expression<Func<TestModel, bool>> initial = x => x.IsActive;
            var result = new ExpressionBuilder<TestModel>(initial)
                .And(x => x.Age >= 18)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true, Age = 20 }).Should().BeTrue();
            compiled(new TestModel { IsActive = false, Age = 20 }).Should().BeFalse();
        }

        [Fact]
        public void Constructor_WithNullExpression_Throws()
        {
            var act = () => new ExpressionBuilder<TestModel>(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void MixedAndOr_RespectsOrder()
        {
            // (IsActive AND Age >= 18) OR Name == "Admin"
            var result = new ExpressionBuilder<TestModel>()
                .And(x => x.IsActive)
                .And(x => x.Age >= 18)
                .Or(x => x.Name == "Admin")
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { IsActive = true, Age = 20, Name = "User" }).Should().BeTrue();
            compiled(new TestModel { IsActive = false, Age = 10, Name = "Admin" }).Should().BeTrue();
            compiled(new TestModel { IsActive = false, Age = 10, Name = "User" }).Should().BeFalse();
        }

        [Fact]
        public void RealWorldFilter_MultipleOptionalConditions()
        {
            // Simulates a typical ProductList-style filter
            string? search = "Widget";
            string? category = null;
            bool? isActive = true;
            int? minAge = null;

            var result = new ExpressionBuilder<TestModel>()
                .AndIf(!string.IsNullOrEmpty(search), x => x.Name!.Contains(search!))
                .AndIf(!string.IsNullOrEmpty(category), x => x.Name!.StartsWith(category!))
                .AndIf(isActive.HasValue, x => x.IsActive == isActive!.Value)
                .AndIf(minAge.HasValue, x => x.Age >= minAge!.Value)
                .Build();

            var compiled = result!.Compile();
            compiled(new TestModel { Name = "Blue Widget", IsActive = true, Age = 5 }).Should().BeTrue();
            compiled(new TestModel { Name = "Blue Widget", IsActive = false, Age = 5 }).Should().BeFalse();
            compiled(new TestModel { Name = "Gadget", IsActive = true, Age = 5 }).Should().BeFalse();
        }
    }
}
