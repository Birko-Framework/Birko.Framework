using System;
using System.Collections.Generic;
using System.Text;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Conditions
{
    /// <summary>
    /// TASK-869 — the patterns a provider's <see cref="SqlValueFragmentDialect.StringMatch"/> binds for a string
    /// <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> / <c>Equals</c> with <see cref="StringComparison.Ordinal"/> or
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>. One producer for the escaping and for C#'s case classes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why no <c>UPPER(col) LIKE UPPER(term)</c>:</b> measured over every character of planes 0 and 1, each server's
    /// <c>UPPER</c> puts characters in a different case class than C#'s <c>OrdinalIgnoreCase</c> does — PostgreSQL 16,
    /// MySQL 181, SQL Server 625 (<c>Latin1_General_100</c>) to 1101 (the default collation). <c>ı</c> and <c>ſ</c> are the
    /// common ones: SQL folds them into <c>I</c> / <c>S</c>, C# keeps each on its own, so <c>Contains("i",
    /// OrdinalIgnoreCase)</c> matched <c>ı</c> on every server. So the term's characters are expanded here into C#'s own
    /// case classes (<c>[iI]</c>), and the row is compared ordinally against that: exact by construction, whatever the
    /// server's Unicode tables.
    /// </para>
    /// </remarks>
    public static class SqlStringMatch
    {
        /// <summary>
        /// The <c>LIKE</c> pattern of an exact (ordinal) match of <paramref name="term"/>: escaped with
        /// <see cref="SqlBuilderContext.LikeEscapeCharacter"/>, wildcards only where <paramref name="kind"/> puts them.
        /// </summary>
        public static string LikePattern(ConditionType kind, string term)
        {
            var escaped = SqlBuilderContext.EscapeLikePattern(term);
            return kind switch
            {
                ConditionType.Like => "%" + escaped + "%",
                ConditionType.StartsWith => escaped + "%",
                ConditionType.EndsWith => "%" + escaped,
                ConditionType.TextEqual => escaped,
                _ => throw UnknownKind(kind),
            };
        }

        /// <summary>
        /// A SQL Server <c>LIKE</c> pattern matching what C#'s <c>OrdinalIgnoreCase</c> matches, when compared under a binary
        /// collation: each cased character becomes the bracket class of its C# case variants (<c>[aA]</c>), everything
        /// else is escaped as in <see cref="LikePattern"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The term holds a letter outside the Basic Multilingual Plane that has case variants: SQL Server compares a
        /// surrogate pair as two code units, and a bracket class of pairs matched nothing (measured, Deseret).
        /// </exception>
        public static string LikeCaseClassPattern(ConditionType kind, string term)
        {
            if (kind is not (ConditionType.Like or ConditionType.StartsWith or ConditionType.EndsWith or ConditionType.TextEqual))
                throw UnknownKind(kind);
            var sb = new StringBuilder(term.Length * 3 + 2);
            if (kind is ConditionType.Like or ConditionType.EndsWith) sb.Append('%');
            foreach (var (text, variants) in Elements(term))
            {
                if (variants == null)
                {
                    sb.Append(SqlBuilderContext.EscapeLikePattern(text));
                    continue;
                }
                sb.Append('[');
                foreach (var variant in variants)
                {
                    if (variant.Length != 1)
                        throw new NotSupportedException(
                            "Cannot translate a string comparison with StringComparison.OrdinalIgnoreCase to SQL Server when the "
                            + "term holds a letter outside the Basic Multilingual Plane that has case variants: SQL Server's LIKE "
                            + "compares it as two UTF-16 code units, and has no class that matches its variants. Use "
                            + "StringComparison.Ordinal, or compare in memory.");
                    sb.Append(variant);
                }
                sb.Append(']');
            }
            if (kind is ConditionType.Like or ConditionType.StartsWith) sb.Append('%');
            return sb.ToString();
        }

        /// <summary>
        /// A regular expression matching what C#'s <c>OrdinalIgnoreCase</c> matches, for an engine that compares literal
        /// characters exactly (PostgreSQL's ARE under <c>COLLATE "C"</c>, MySQL's ICU <c>REGEXP_LIKE(…, 'c')</c>): each cased
        /// character becomes the bracket class of its C# case variants, every other ASCII punctuation character is
        /// backslash-quoted, and the match is anchored with <paramref name="start"/> / <paramref name="end"/> as
        /// <paramref name="kind"/> requires.
        /// </summary>
        /// <param name="start">The engine's start-of-string anchor (<c>^</c> in PostgreSQL, <c>\A</c> in ICU).</param>
        /// <param name="end">
        /// The engine's end-of-string anchor (<c>$</c> in PostgreSQL, <c>\z</c> in ICU, whose <c>$</c> also matches before a
        /// final line terminator).
        /// </param>
        public static string RegexCaseClassPattern(ConditionType kind, string term, string start, string end)
        {
            if (kind is not (ConditionType.Like or ConditionType.StartsWith or ConditionType.EndsWith or ConditionType.TextEqual))
                throw UnknownKind(kind);
            // An empty pattern is an error in MySQL (measured); every string contains, starts and ends with the empty one.
            if (term.Length == 0) return kind == ConditionType.TextEqual ? start + end : start;
            var sb = new StringBuilder(term.Length * 3 + 4);
            if (kind is ConditionType.StartsWith or ConditionType.TextEqual) sb.Append(start);
            foreach (var (text, variants) in Elements(term))
            {
                if (variants != null)
                {
                    sb.Append('[');
                    foreach (var variant in variants) sb.Append(variant);
                    sb.Append(']');
                    continue;
                }
                if (text.Length == 1 && text[0] < 128 && !char.IsLetterOrDigit(text[0])) sb.Append('\\');
                sb.Append(text);
            }
            if (kind is ConditionType.EndsWith or ConditionType.TextEqual) sb.Append(end);
            return sb.ToString();
        }

        /// <summary>
        /// The characters of <paramref name="term"/> (a surrogate pair as one), each with its C# <c>OrdinalIgnoreCase</c>
        /// variants when it has any besides itself, else null.
        /// </summary>
        public static IEnumerable<(string Text, IReadOnlyList<string>? Variants)> Elements(string term)
        {
            var classes = CaseClasses.Value;
            for (var i = 0; i < term.Length; i++)
            {
                string text;
                if (char.IsHighSurrogate(term[i]) && i + 1 < term.Length && char.IsLowSurrogate(term[i + 1]))
                {
                    text = term.Substring(i, 2);
                    i++;
                }
                else
                    text = term[i].ToString();
                var codePoint = text.Length == 2 || !char.IsSurrogate(text[0]) ? char.ConvertToUtf32(text, 0) : -1;
                yield return (text, codePoint >= 0 && classes.TryGetValue(codePoint, out var variants) ? variants : null);
            }
        }

        /// <summary>
        /// Every code point with an <c>OrdinalIgnoreCase</c> twin, mapped to its whole class. C#'s comparison folds a
        /// character to its invariant upper case only where that is a simple, single-character mapping it accepts (it keeps
        /// <c>ı</c> and <c>ſ</c> apart from <c>I</c> and <c>S</c>, measured), so a character's class key is its invariant upper
        /// case when the comparison calls the two equal, else the character itself. Planes 0 and 1 hold every cased
        /// character Unicode has; built once, on first use.
        /// </summary>
        private static readonly Lazy<Dictionary<int, IReadOnlyList<string>>> CaseClasses = new(() =>
        {
            var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var codePoint = 0; codePoint <= 0x1FFFF; codePoint++)
            {
                if (codePoint is >= 0xD800 and <= 0xDFFF) continue;
                var text = char.ConvertFromUtf32(codePoint);
                var upper = text.ToUpperInvariant();
                var key = string.Equals(text, upper, StringComparison.OrdinalIgnoreCase) ? upper : text;
                if (!byKey.TryGetValue(key, out var members)) byKey[key] = members = new List<string>(2);
                members.Add(text);
            }
            var classes = new Dictionary<int, IReadOnlyList<string>>();
            foreach (var members in byKey.Values)
            {
                if (members.Count < 2) continue;
                var frozen = members.AsReadOnly();
                foreach (var member in members) classes[char.ConvertToUtf32(member, 0)] = frozen;
            }
            return classes;
        });

        private static ArgumentOutOfRangeException UnknownKind(ConditionType kind)
            => new(nameof(kind), kind, "A string match is Like (Contains), StartsWith, EndsWith or TextEqual.");
    }
}
