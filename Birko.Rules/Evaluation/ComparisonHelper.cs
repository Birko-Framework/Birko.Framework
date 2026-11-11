using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Birko.Rules;

/// <summary>
/// Type-safe comparison logic for rule evaluation.
/// Handles numeric promotion, string comparison, null checks, and collection membership.
/// </summary>
internal static class ComparisonHelper
{
    /// <summary>
    /// Compare two values using the specified operator. Returns true if condition is satisfied.
    /// </summary>
    public static bool Compare(object? actual, ComparisonOperator op, object? expected, object? upperExpected = null)
    {
        // TASK-882: the rule's value as a value of the member's type, as the compiled rule reads it; none means no answer.
        if (!TryAlignOperands(actual, op, ref expected, ref upperExpected))
            return false;

        return op switch
        {
            ComparisonOperator.IsNull => actual is null,
            ComparisonOperator.IsNotNull => actual is not null,
            ComparisonOperator.Equal => AreEqual(actual, expected),
            ComparisonOperator.NotEqual => !AreEqual(actual, expected),
            // TASK-881: an ordering with NULL on either side is false, as C#'s lifted operators answer (and the compiled
            // rule, and SQL since TASK-864). NULL used to order below every value: `Score LessThan 5` selected the NULL
            // rows, `Score GreaterThan null` every non-null row. A negated ordering is TRUE on NULL (`!(null < 5)`),
            // which is CanEvaluate's job to allow, not this method's.
            ComparisonOperator.GreaterThan => CompareValues(actual, expected) > 0,
            ComparisonOperator.GreaterThanOrEqual => CompareValues(actual, expected) >= 0,
            ComparisonOperator.LessThan => CompareValues(actual, expected) < 0,
            ComparisonOperator.LessThanOrEqual => CompareValues(actual, expected) <= 0,
            ComparisonOperator.Between => CompareValues(actual, expected) >= 0 && CompareValues(actual, upperExpected) <= 0,
            // A string operator against a NON-string member is not a predicate this engine can answer,
            // so it matches nothing — in BOTH polarities. `NotContains` deliberately does NOT read as
            // `!Contains` here: negating "cannot be evaluated" is what turns match-none into match-ALL,
            // which is exactly how SH-H043/SH-H044 emptied tables on the expression side (TASK-116).
            //
            // This is the convergence point chosen for the two engines. The expression path cannot move
            // to meet this one: it runs against a database, and no portable translation of
            // `column.ToString().Contains(…)` exists across the SQL providers and ElasticSearch. The
            // behaviour given up is `21 Contains "1"` → true, which was an artefact of ToString() rather
            // than a business predicate anybody specified.
            ComparisonOperator.Contains => IsStringMember(actual) && ContainsString(actual, expected),
            // SH-M296 (TASK-171): a null needle is no predicate, so NotContains does not invert it into match-ALL; it
            // matches nothing, as Contains does and as the expression engine does.
            ComparisonOperator.NotContains => IsStringMember(actual) && expected is not null && !ContainsString(actual, expected),
            ComparisonOperator.StartsWith => IsStringMember(actual) && StartsWithString(actual, expected),
            ComparisonOperator.EndsWith => IsStringMember(actual) && EndsWithString(actual, expected),
            ComparisonOperator.Like => IsStringMember(actual) && LikeString(actual, expected),
            // TASK-156: a null set is no predicate (the translator degrades it), so NotIn does not invert it into match-ALL.
            ComparisonOperator.In => IsIn(actual, expected),
            ComparisonOperator.NotIn => expected is not null && !IsIn(actual, expected),
            _ => false
        };
    }

    /// <summary>
    /// Whether a string operator can be answered against this value at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Null counts as string-compatible, deliberately.</b> This engine sees values, not declared
    /// types, so a null gives it nothing to reject on — and the expression path (which IS typed) answers
    /// `true` for `NotContains` over a null string member, with a test pinning that. Treating null as
    /// non-string here would agree with nothing and would newly diverge on the very case the two engines
    /// currently match on.</para>
    /// <para>Residual, narrow and known: a null-valued <i>non-string</i> member (e.g. `int? Qty = null`)
    /// still answers `true` for `NotContains` here while the expression path answers match-none, because
    /// only the typed side can tell those two nulls apart. Closing it needs the declared property type
    /// plumbed into the rule context, which is a bigger change than this correctness fix.</para>
    /// </remarks>
    private static bool IsStringMember(object? actual) => actual is null or string;

    /// <summary>
    /// Whether <see cref="Compare"/> could actually answer this operator against this value, as opposed to
    /// returning <c>false</c> because it had nothing to say. Callers applying <c>IsNegated</c> MUST consult
    /// this: negating "cannot be evaluated" produces match-<b>all</b>, which is the whole species of defect
    /// behind SH-H041…SH-H044. Mirrors <c>RuleSpecification.Leaf.Degraded</c> on the expression side, so the
    /// two engines enforce the invariant the same way rather than agreeing by coincidence.
    /// </summary>
    /// <remarks>
    /// TASK-156: also false for what the rule-to-LINQ translator degrades, so a NEGATED rule does not invert "cannot be
    /// evaluated" into match-all in memory while the store matches nothing: a null needle and a null set.
    /// TASK-882: also false for a value with no form for the member's type (<see cref="TryAlignOperands"/>), which the
    /// compiled rule degrades the same way. A <c>Like</c> pattern with an interior <c>%</c> never gets here:
    /// <see cref="RuleEvaluator"/> refuses it by name first (<see cref="RuleLikePattern"/>).
    /// </remarks>
    public static bool CanEvaluate(object? actual, ComparisonOperator op, object? expected = null, object? upperExpected = null) => op switch
    {
        ComparisonOperator.Contains
            or ComparisonOperator.NotContains
            or ComparisonOperator.StartsWith
            or ComparisonOperator.EndsWith => IsStringMember(actual) && expected is not null,
        ComparisonOperator.Like => IsStringMember(actual) && RuleLikePattern.Parse(expected, string.Empty) is not null,
        ComparisonOperator.In or ComparisonOperator.NotIn => expected is not null && TryAlignOperands(actual, op, ref expected, ref upperExpected),
        _ => TryAlignOperands(actual, op, ref expected, ref upperExpected)
    };

    /// <summary>
    /// TASK-882 — rewrites the rule's value(s) into the type of <paramref name="actual"/> through
    /// <see cref="RuleValueConverter"/>, the producer the compiled rule and the SQL rule read values through, for the
    /// comparison operators. False when a value has no such form (an unknown enum name, text that is no Guid or number):
    /// no answer, which <see cref="CanEvaluate"/> keeps from being negated into a match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before (measured): an enum compared with a number was unequal (<c>State Equal 1</c> matched nothing, where the
    /// compiled rule and SQL match <c>Active</c>), an enum against its name ordered as text (<c>Closed</c> before
    /// <c>Pending</c>), a <c>DateTime</c> against text compared the host culture's rendering, a Guid only in its "D" text form,
    /// and a value that is no number (<c>Amount NotEqual "abc"</c>) matched every row where the compiled rule matches none.
    /// </para>
    /// <para>
    /// Applied only where the member's value is typed beyond what this engine already compares: an enum, a Guid, a date, or
    /// any non-string value against text. Two numbers keep their exact / double promotion (SH-M360, SH-M294). This engine
    /// sees values, not declared types, so a NULL member has no type to align to and answers as before.
    /// </para>
    /// </remarks>
    private static bool TryAlignOperands(object? actual, ComparisonOperator op, ref object? expected, ref object? upperExpected)
    {
        switch (op)
        {
            case ComparisonOperator.Equal
                or ComparisonOperator.NotEqual
                or ComparisonOperator.GreaterThan
                or ComparisonOperator.GreaterThanOrEqual
                or ComparisonOperator.LessThan
                or ComparisonOperator.LessThanOrEqual:
                return TryAlign(actual, expected, out expected);
            case ComparisonOperator.Between:
                return TryAlign(actual, expected, out expected) && TryAlign(actual, upperExpected, out upperExpected);
            case ComparisonOperator.In or ComparisonOperator.NotIn:
                if (expected is not IEnumerable set || expected is string)
                    return TryAlign(actual, expected, out expected);
                var aligned = new List<object?>();
                foreach (var item in set)
                {
                    if (!TryAlign(actual, item, out var one))
                        return false;
                    aligned.Add(one);
                }
                expected = aligned;
                return true;
            default:
                return true;
        }
    }

    private static bool TryAlign(object? actual, object? expected, out object? aligned)
    {
        aligned = expected;
        if (actual is null || expected is null || actual is string)
            return true;
        var type = actual.GetType();
        if (type.IsInstanceOfType(expected))
            return true;
        var typed = type.IsEnum || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset);
        if (!typed && expected is not string)
            return true;
        return RuleValueConverter.TryConvert(expected, type, out aligned);
    }

    private static bool AreEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        // TASK-895 (the user's decision, 2026-11-10: a rule's string equality is exact): two strings compare ordinally, as
        // the compiled rule's == and, since TASK-873 / TASK-895, every SQL provider do. It was OrdinalIgnoreCase (SH-M295's
        // recorded divergence, TASK-144): `Status Equal "open"` matched "Open" here and not at the store. Equal, NotEqual,
        // In and NotIn all come here; the string methods (Contains, StartsWith, EndsWith, Like) keep ignoring case.
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);

        // SH-M360 (TASK-156): two exact numbers (integers, decimal) compare exactly, as decimal. Promoting both to
        // double equated 9007199254740993L with ...992L and 1.0000000000000000001m with 1.0.
        if (TryToExact(a, out var ea) && TryToExact(b, out var eb))
            return ea == eb;

        // Try numeric comparison (handles int vs double, decimal vs long, etc.)
        if (TryNumeric(a, b, out var da, out var db))
        {
            // CR-L333: the old `< double.Epsilon` (~4.9e-324) was effectively strict equality and gave no
            // room for float→double promotion error — the float 0.1f promoted via TryToDouble did not
            // compare equal to the double literal 0.1, contradicting this engine's advertised numeric
            // promotion. Apply a scaled relative tolerance ONLY to fractional values; integral values
            // (int/long/whole decimals) stay exact so two distinct integers never falsely match — the audit
            // scoped the concern to float/double literals, and a flat relative tolerance would wrongly equate
            // large integers (e.g. 1e9 vs 1e9+1).
            var diff = Math.Abs(da - db);
            if (diff == 0d)
            {
                return true;
            }
            bool bothIntegral = da == Math.Truncate(da) && db == Math.Truncate(db);
            if (bothIntegral)
            {
                return false;
            }
            return diff <= 1e-6 * Math.Max(Math.Abs(da), Math.Abs(db));
        }

        return a.Equals(b) || string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The order of two values, or <c>null</c> when either is null: C#'s lifted comparison has no order for NULL, so every
    /// ordering over it (<c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c>) is false, and a lifted <c>int?</c> compared
    /// with <c>null</c> is false on both sides (TASK-881). The one producer of that answer for this engine: each ordering
    /// operator compares this with an <c>int</c>, and C# lifts that comparison to false.
    /// </summary>
    private static int? CompareValues(object? a, object? b)
    {
        if (a is null || b is null) return null;

        // SH-M360 (TASK-156): exact numbers order exactly.
        if (TryToExact(a, out var ea) && TryToExact(b, out var eb))
            return ea.CompareTo(eb);

        // Numeric comparison with promotion
        if (TryNumeric(a, b, out var da, out var db))
            return da.CompareTo(db);

        // DateTime
        if (a is DateTime dtA && b is DateTime dtB)
            return dtA.CompareTo(dtB);

        // IComparable fallback
        if (a is IComparable comparable)
        {
            try { return comparable.CompareTo(b); }
            catch { /* type mismatch, fall through to string */ }
        }

        // String fallback
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsString(object? actual, object? expected)
    {
        if (actual is null || expected is null) return false;
        return actual.ToString()!.Contains(expected.ToString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithString(object? actual, object? expected)
    {
        if (actual is null || expected is null) return false;
        return actual.ToString()!.StartsWith(expected.ToString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndsWithString(object? actual, object? expected)
    {
        if (actual is null || expected is null) return false;
        return actual.ToString()!.EndsWith(expected.ToString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LikeString(object? actual, object? expected)
    {
        // TASK-882: RuleLikePattern, the one parser of a Like pattern (TASK-156's anchors; a lone "%" is "any value", SH-M359).
        if (actual is null || RuleLikePattern.Parse(expected, string.Empty) is not { } pattern) return false;
        return pattern.IsMatch(actual.ToString());
    }

    /// <summary>
    /// Whether <paramref name="actual"/> equals one of the candidates, each by <see cref="AreEqual"/>, so a NULL value is
    /// in a list holding <c>null</c> (<c>x == null || x == 5</c>, TASK-881) and in no other. A null set is no predicate
    /// (<see cref="CanEvaluate"/>); it never reaches a candidate.
    /// </summary>
    private static bool IsIn(object? actual, object? expected)
    {
        if (expected is null) return false;

        if (expected is IEnumerable collection and not string)
        {
            foreach (var item in collection)
            {
                if (AreEqual(actual, item)) return true;
            }
            return false;
        }

        // Single value comparison
        return AreEqual(actual, expected);
    }

    /// <summary>An integer or a decimal, as an exact decimal (SH-M360).</summary>
    private static bool TryToExact(object value, out decimal result)
    {
        switch (value)
        {
            case decimal m: result = m; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = ul; return true;
            default: result = 0; return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = ul; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>
    /// Both sides as doubles, for a numeric comparison. A string is read as a number only when the OTHER side is a number
    /// (a rule value typed as text against a numeric field), and only as a plain finite number.
    /// </summary>
    /// <remarks>
    /// SH-M361 (TASK-156): every string was tried as a number with <c>NumberStyles.Any</c>, so two string codes compared
    /// numerically ("007" == "7", "(5)" == "-5", "1,000" == "1000"), where the store compares them as text.
    /// </remarks>
    private static bool TryNumeric(object a, object b, out double da, out double db)
    {
        var aNumber = TryToDouble(a, out da);
        var bNumber = TryToDouble(b, out db);
        if (aNumber && bNumber) return true;
        if (aNumber && b is string bs) return TryParseNumber(bs, out db);
        if (bNumber && a is string @as) return TryParseNumber(@as, out da);
        return false;
    }

    private static bool TryParseNumber(string text, out double result)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
}
