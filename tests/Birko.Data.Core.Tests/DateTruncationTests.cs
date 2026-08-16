using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-224 — CosmosDB stores a <see cref="DateTime"/> as an ISO string and its LINQ provider translates
/// <c>.Date</c> as a JSON <i>sub-property access</i>: <c>x.CreatedAt.Date == d</c> emitted
/// <c>WHERE (root["CreatedAt"]["Date"] = "…")</c>, which addresses a member of a string. The query is
/// valid, runs, and returns <b>zero rows with no error</b>.
///
/// The rewrite turns it into a half-open range over the raw member. Meaning is the oracle throughout:
/// every case compares the rewritten predicate against the ORIGINAL lambda compiled, over boundary
/// instants, because an off-by-one in a range is exactly the mistake this could make and no shape
/// assertion would catch it.
/// </summary>
public class DateTruncationTests
{
    public class Model { public DateTime When { get; set; } public DateTime? MaybeWhen { get; set; } }

    private static readonly DateTime D = new(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Boundary instants around D — the day before, midnight, mid-day, and midnight of D+1.</summary>
    private static IEnumerable<DateTime> Probes() => new[]
    {
        D.AddDays(-1), D.AddTicks(-1), D, D.AddHours(12), D.AddDays(1).AddTicks(-1), D.AddDays(1), D.AddDays(2),
    };

    [Theory]
    [MemberData(nameof(AllOperators))]
    public void Every_operator_preserves_meaning(string label, Expression<Func<Model, bool>> e)
    {
        var original = e.Compile();
        var rewritten = DateTruncation.Rewrite(e)!.Compile();

        foreach (var t in Probes())
        {
            var m = new Model { When = t };
            rewritten(m).Should().Be(original(m), $"{label} at {t:O}");
        }
    }

    public static TheoryData<string, Expression<Func<Model, bool>>> AllOperators() => new()
    {
        // The task required every comparison operator to be handled or explicitly refused — not just ==.
        { "==", x => x.When.Date == D },
        { "!=", x => x.When.Date != D },
        { "<",  x => x.When.Date < D },
        { "<=", x => x.When.Date <= D },
        { ">",  x => x.When.Date > D },
        { ">=", x => x.When.Date >= D },
        // Member on the RIGHT — the operator must mirror, or every inequality inverts.
        { "reversed <", x => D < x.When.Date },
        { "reversed >=", x => D >= x.When.Date },
        { "reversed ==", x => D == x.When.Date },
    };

    [Fact]
    public void The_Date_member_is_gone_from_the_tree()
    {
        // The behavioural tests above run in-process, where .Date works fine. This is the one that
        // pins what actually mattered: the driver must never see the sub-property access.
        DateTruncation.Rewrite<Model>(x => x.When.Date == D)!
            .ToString().Should().NotContain(".Date");
    }

    [Fact]
    public void A_nullable_member_is_rewritten_and_keeps_null_semantics()
    {
        Expression<Func<Model, bool>> e = x => x.MaybeWhen!.Value.Date == D;

        var rewritten = DateTruncation.Rewrite(e);

        // Whether or not the shape is recognised, meaning must hold — including that null matches nothing.
        var original = e.Compile();
        var after = rewritten!.Compile();
        foreach (var t in Probes())
        {
            var m = new Model { MaybeWhen = t };
            after(m).Should().Be(original(m), $"nullable at {t:O}");
        }
        // On null, `.Value` throws — in the ORIGINAL lambda too. Parity is the contract, so assert the
        // outcomes match rather than picking one: expecting `false` here was my own error, caught by
        // running it. The realistic spelling guards first, covered below.
        Record.Exception(() => after(new Model { MaybeWhen = null })).Should().BeOfType<InvalidOperationException>();
        Record.Exception(() => original(new Model { MaybeWhen = null })).Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void The_guarded_nullable_spelling_round_trips()
    {
        // How a caller actually writes it: null-check, then compare. This must survive the rewrite,
        // including the short-circuit that keeps `.Value` from ever being reached on a null.
        Expression<Func<Model, bool>> e = x => x.MaybeWhen != null && x.MaybeWhen.Value.Date == D;

        var rewritten = DateTruncation.Rewrite(e)!;
        var after = rewritten.Compile();

        rewritten.ToString().Should().NotContain(".Date");
        after(new Model { MaybeWhen = null }).Should().BeFalse();
        after(new Model { MaybeWhen = D.AddHours(5) }).Should().BeTrue();
        after(new Model { MaybeWhen = D.AddDays(1) }).Should().BeFalse();
    }

    [Fact]
    public void A_comparison_of_two_dates_is_left_alone()
    {
        // Member-vs-member is a real comparison of two stored values, not a day bucket. There is no
        // half-open range to become, and rewriting it would change its meaning.
        Expression<Func<Model, bool>> e = x => x.When.Date == x.MaybeWhen!.Value.Date;

        DateTruncation.Rewrite(e).Should().BeSameAs(e);
    }

    [Fact]
    public void A_predicate_with_no_Date_truncation_is_returned_unchanged()
    {
        Expression<Func<Model, bool>> e = x => x.When > D;

        DateTruncation.Rewrite(e).Should().BeSameAs(e);
    }

    [Fact]
    public void A_null_predicate_stays_null() => DateTruncation.Rewrite<Model>(null).Should().BeNull();

    [Fact]
    public void A_Date_truncation_nested_in_a_larger_predicate_is_rewritten()
    {
        Expression<Func<Model, bool>> e = x => x.When.Date == D && x.MaybeWhen != null;

        var rewritten = DateTruncation.Rewrite(e)!;

        rewritten.ToString().Should().NotContain(".Date");
        rewritten.Compile()(new Model { When = D.AddHours(3), MaybeWhen = D }).Should().BeTrue();
        rewritten.Compile()(new Model { When = D.AddDays(1), MaybeWhen = D }).Should().BeFalse();
    }
}
