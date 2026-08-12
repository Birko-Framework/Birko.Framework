using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-111, follow-up found in review of the fix itself.
///
/// <c>DataBase.ResolveRuleField</c> resolves a rule's field **at the caller**, before any store operation
/// has run. A <c>[View]</c> type carries no <c>[Table]</c>, so <c>LoadTable</c> returns null and the
/// <c>ResolveFieldSelectName</c> delegate is the only thing that can resolve its fields — but that delegate
/// used to be registered solely inside <c>LoadView</c>. So the *first* <c>ToConditions&lt;MyView&gt;(…)</c>
/// in a process threw <c>ArgumentException</c> naming a perfectly valid view field, while the identical
/// call succeeded later once anything had touched a view.
///
/// The ORDER BY twin never had this: <c>ResolveOrderFields</c> is invoked by the connector, after
/// <c>LoadView</c>. Moving resolution to the caller is what exposed it — a first-call-only, order-dependent
/// failure, which is the worst kind to diagnose from a bug report.
///
/// A <c>[ModuleInitializer]</c> now registers the resolver at module load.
/// </summary>
public class ViewResolverRegistrationTests
{
    [Fact]
    public void The_view_field_resolver_is_registered_without_anything_having_loaded_a_view()
    {
        // Behavioural assertion. On its own this is weak evidence — xUnit shares a process, so a sibling
        // test that touched a view would satisfy it regardless. It is paired with the mechanism check
        // below, which cannot pass by accident.
        DataBase.ResolveFieldSelectName.Should().NotBeNull(
            "a rule over a [View] type resolves through this delegate and nothing else, and rule fields "
            + "resolve at the caller — before any LoadView has run");
    }

    [Fact]
    public void The_registration_is_a_module_initializer_not_a_side_effect_of_LoadView()
    {
        // This is the check that actually fails if the fix is reverted: it pins the mechanism rather than
        // an end state a sibling test could have produced.
        var initializer = typeof(DataBase)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "InitializeViewResolver");

        initializer.Should().NotBeNull(
            "the view resolver must be registered at module load, not on the first LoadView call");
        initializer!.GetCustomAttribute<ModuleInitializerAttribute>().Should().NotBeNull(
            "without [ModuleInitializer] the registration is back to being a side effect of LoadView, and "
            + "the first ToConditions<TView>(…) in a process throws for a valid field");
        initializer.GetParameters().Should().BeEmpty("a module initializer takes no arguments");
        initializer.ReturnType.Should().Be(typeof(void));
    }
}
