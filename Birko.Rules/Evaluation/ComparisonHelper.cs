using System;
using System.Collections;
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
        return op switch
        {
            ComparisonOperator.IsNull => actual is null,
            ComparisonOperator.IsNotNull => actual is not null,
            ComparisonOperator.Equal => AreEqual(actual, expected),
            ComparisonOperator.NotEqual => !AreEqual(actual, expected),
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
            ComparisonOperator.NotContains => IsStringMember(actual) && !ContainsString(actual, expected),
            ComparisonOperator.StartsWith => IsStringMember(actual) && StartsWithString(actual, expected),
            ComparisonOperator.EndsWith => IsStringMember(actual) && EndsWithString(actual, expected),
            ComparisonOperator.Like => IsStringMember(actual) && LikeString(actual, expected),
            ComparisonOperator.In => IsIn(actual, expected),
            ComparisonOperator.NotIn => !IsIn(actual, expected),
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
    public static bool CanEvaluate(object? actual, ComparisonOperator op) => op switch
    {
        ComparisonOperator.Contains
            or ComparisonOperator.NotContains
            or ComparisonOperator.StartsWith
            or ComparisonOperator.EndsWith
            or ComparisonOperator.Like => IsStringMember(actual),
        _ => true
    };

    private static bool AreEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        // Try numeric comparison (handles int vs double, decimal vs long, etc.)
        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
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

    private static int CompareValues(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // Numeric comparison with promotion
        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
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
        // Simple % wildcard matching (SQL LIKE style)
        if (actual is null || expected is null) return false;
        var pattern = expected.ToString()!;
        var value = actual.ToString()!;

        if (pattern.StartsWith('%') && pattern.EndsWith('%'))
            return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('%'))
            return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('%'))
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIn(object? actual, object? expected)
    {
        if (actual is null || expected is null) return false;

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

    private static bool TryToDouble(object value, out double result)
    {
        if (value is double d) { result = d; return true; }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is float f) { result = f; return true; }
        if (value is decimal m) { result = (double)m; return true; }
        if (value is short s) { result = s; return true; }
        if (value is byte b) { result = b; return true; }

        return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }
}
