using System;
using System.Globalization;

namespace Birko.Rules;

/// <summary>
/// TASK-882 — the one producer of "this rule value, as a value of that member's type". A rule value arrives as data (JSON, a
/// rule builder, a form): an enum by its name or its number, a Guid or a date as text, a number as text. Every runner of a
/// rule reads it through here, so one rule means one value wherever it runs: <see cref="RuleExpressionConverter"/> (the
/// constant of the compiled rule), <see cref="RuleEvaluator"/> (against the value it reads) and the SQL rule converter,
/// which then binds the column's stored form (<c>AbstractField.ToStoredValue</c>, the lambda path's producer).
/// </summary>
/// <remarks>
/// Before (measured, TASK-882): only the compiled rule converted. The SQL rule bound the raw value, so <c>State Equal
/// "Active"</c> against an enum stored as its number failed or matched other rows depending on the provider, and a Guid
/// as text missed every row where the column holds a Guid; the evaluator compared an enum with a number as unequal and a
/// date with text in the host's culture.
/// </remarks>
public static class RuleValueConverter
{
    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/> (or its underlying type, for a <c>Nullable&lt;T&gt;</c>),
    /// invariantly. False when there is no such value: a null for a type that cannot hold one, text that does not parse
    /// (an unknown enum name, a malformed Guid), a number out of range. Callers treat that as a rule that cannot be
    /// evaluated (it matches nothing, negated or not, TASK-116), never as a guess.
    /// </summary>
    /// <remarks>
    /// SH-M292 / SH-M293 (TASK-171): <c>Convert.ChangeType</c> cannot make a Guid or an enum from text, and used the host's
    /// culture. An enum name is matched ignoring case; a number becomes the enum member of that value.
    /// </remarks>
    public static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        converted = null;
        if (value is null)
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;

        if (targetType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            converted = value switch
            {
                string guidText when underlying == typeof(Guid) => Guid.Parse(guidText),
                string dateText when underlying == typeof(DateTime) => DateTime.Parse(dateText, CultureInfo.InvariantCulture),
                string dtoText when underlying == typeof(DateTimeOffset) => DateTimeOffset.Parse(dtoText, CultureInfo.InvariantCulture),
                string enumText when underlying.IsEnum => Enum.Parse(underlying, enumText, ignoreCase: true),
                _ when underlying.IsEnum => Enum.ToObject(underlying, value),
                _ => Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture),
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            converted = null;
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> is a fraction compared with an integral member: it is compared as a number, not
    /// converted. SH-M294 (TASK-171): <c>ChangeType(10.7, int)</c> is 11, so <c>Qty GreaterThan 10.7</c> would select 11.
    /// </summary>
    public static bool ComparesAsFraction(object? value, Type memberType)
    {
        if (memberType == null)
            throw new ArgumentNullException(nameof(memberType));
        return IsFractional(value) && IsIntegral(memberType);
    }

    private static bool IsFractional(object? value) => value switch
    {
        double d => d != Math.Truncate(d),
        float f => f != MathF.Truncate(f),
        decimal m => m != decimal.Truncate(m),
        _ => false,
    };

    private static bool IsIntegral(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
    }
}
