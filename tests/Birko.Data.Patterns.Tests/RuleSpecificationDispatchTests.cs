using Birko.Data.Patterns.Specification;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-H074: RuleSpecification.IsSatisfiedBy was declared `new`, not `override`, so callers holding
/// an ISpecification&lt;T&gt;/Specification&lt;T&gt; reference silently ran the base compiled-expression
/// path instead of the RuleEvaluator. The two paths disagree (the compiled path calls String.Contains
/// on a null member -> NRE; the evaluator null-guards -> no match). These tests pin polymorphic
/// dispatch to the evaluator through every reference type.
/// </summary>
public class RuleSpecificationDispatchTests
{
    private class Person
    {
        public string? Name { get; set; }
    }

    private static RuleSpecification<Person> ContainsName(string needle)
        => new(new Rule(nameof(Person.Name), ComparisonOperator.Contains, needle));

    [Fact]
    public void InterfaceReference_UsesEvaluator_NullMember_DoesNotThrow()
    {
        // Before the fix this dispatched to the base compiled expression, which invokes
        // string.Contains on a null Name and throws NullReferenceException.
        ISpecification<Person> spec = ContainsName("abc");
        var entity = new Person { Name = null };

        spec.Invoking(s => s.IsSatisfiedBy(entity)).Should().NotThrow();
        spec.IsSatisfiedBy(entity).Should().BeFalse();
    }

    [Fact]
    public void BaseReference_UsesEvaluator_NullMember_DoesNotThrow()
    {
        Specification<Person> spec = ContainsName("abc");
        var entity = new Person { Name = null };

        spec.Invoking(s => s.IsSatisfiedBy(entity)).Should().NotThrow();
        spec.IsSatisfiedBy(entity).Should().BeFalse();
    }

    [Fact]
    public void AllReferenceTypes_AgreeOnMatch()
    {
        var spec = ContainsName("abc");
        var entity = new Person { Name = "xx-ABC-xx" }; // case-insensitive Contains

        bool viaConcrete = spec.IsSatisfiedBy(entity);
        bool viaBase = ((Specification<Person>)spec).IsSatisfiedBy(entity);
        bool viaInterface = ((ISpecification<Person>)spec).IsSatisfiedBy(entity);

        viaConcrete.Should().BeTrue();
        viaBase.Should().Be(viaConcrete);
        viaInterface.Should().Be(viaConcrete);
    }
}
