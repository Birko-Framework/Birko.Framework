using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.ElasticSearch.Extensions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// CR-M088: MultiMatch/MoreLikeThis are query-DSL marker methods translated by name inside store
/// filter expressions and never executed. Their old bodies tried to Compile() a parameter-referencing
/// MemberExpression and threw an opaque runtime error; they now throw a clear NotSupportedException if
/// invoked directly.
/// </summary>
public class EnumerableExtensionsMarkerTests
{
    private static readonly IEnumerable<MemberExpression> Fields = Array.Empty<MemberExpression>();

    [Fact]
    public void MultiMatch_invoked_directly_throws_NotSupported()
    {
        Action act = () => Fields.MultiMatch("value");

        act.Should().Throw<NotSupportedException>().WithMessage("*marker*");
    }

    [Fact]
    public void MoreLikeThis_invoked_directly_throws_NotSupported()
    {
        Action act = () => Fields.MoreLikeThis(new[] { "value" });

        act.Should().Throw<NotSupportedException>().WithMessage("*marker*");
    }
}
