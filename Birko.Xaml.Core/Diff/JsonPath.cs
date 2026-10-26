using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Birko.Xaml.Core.Diff;

/// <summary>
/// Path formatting and pattern matching for <see cref="JsonDiff"/> — a port of <c>formatJsonPath</c> /
/// <c>matchJsonPath</c> in Birko.Web.Core's <c>json-diff.ts</c>, pinned by the shared vectors.
/// </summary>
/// <remarks>
/// Paths read like property access: <c>Items[2].Ref</c>, a key that is not an identifier quoted as
/// <c>["a.b"]</c>, the root as <c>""</c>. Patterns add wildcards: <c>*</c> is any one segment, <c>[*]</c> any one
/// index, <c>**</c> any number of segments (including none). A pattern matches the WHOLE path.
/// </remarks>
public static class JsonPath
{
    private enum TokenKind { Key, Index, Any, AnyIndex, Deep }

    private readonly record struct Token(TokenKind Kind, string? Key = null, long Index = 0);

    private static readonly ConcurrentDictionary<string, Token[]> PatternCache = new(StringComparer.Ordinal);

    /// <summary>Formats segments as a path string: <c>["Items", 2, "Ref"]</c> → <c>Items[2].Ref</c>.</summary>
    public static string Format(IEnumerable<JsonPathSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var s in segments)
        {
            if (s.IsIndex) sb.Append('[').Append(s.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            else if (IsIdentifier(s.Key!)) { if (sb.Length > 0) sb.Append('.'); sb.Append(s.Key); }
            else sb.Append('[').Append(JsString(s.Key!)).Append(']');
        }
        return sb.ToString();
    }

    /// <summary>Whether a path (as segments) matches a pattern. Throws <see cref="FormatException"/> on a malformed pattern.</summary>
    public static bool Matches(string pattern, IReadOnlyList<JsonPathSegment> segments) =>
        MatchTokens(Parse(pattern), 0, segments, 0);

    /// <summary>Validates a pattern, throwing <see cref="FormatException"/> when it is malformed.</summary>
    public static void Validate(string pattern) => Parse(pattern);

    private static Token[] Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (PatternCache.TryGetValue(pattern, out var cached)) return cached;
        var tokens = new List<Token>();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '.') { i++; continue; }
            if (c == '[')
            {
                var close = i + 1 < pattern.Length && pattern[i + 1] == '"' ? FindQuoteEnd(pattern, i + 1) + 1 : pattern.IndexOf(']', i);
                if (close < 0 || close >= pattern.Length || pattern[close] != ']') throw Invalid(pattern);
                var inner = pattern.Substring(i + 1, close - i - 1);
                if (inner == "*") tokens.Add(new Token(TokenKind.AnyIndex));
                else if (inner.Length > 0 && inner.All(char.IsAsciiDigit))
                    // An index too large for an array never matches, as on the web.
                    tokens.Add(new Token(TokenKind.Index, Index: long.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1));
                else if (inner.StartsWith('"')) tokens.Add(new Token(TokenKind.Key, ParseJsonString(inner, pattern)));
                else throw Invalid(pattern);
                i = close + 1;
                continue;
            }
            var end = i;
            while (end < pattern.Length && pattern[end] != '.' && pattern[end] != '[') end++;
            var name = pattern.Substring(i, end - i);
            tokens.Add(name switch
            {
                "**" => new Token(TokenKind.Deep),
                "*" => new Token(TokenKind.Any),
                _ => new Token(TokenKind.Key, name),
            });
            i = end;
        }
        var result = tokens.ToArray();
        PatternCache[pattern] = result;
        return result;
    }

    private static int FindQuoteEnd(string s, int open)
    {
        for (var i = open + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return -1;
    }

    private static string ParseJsonString(string quoted, string pattern)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(quoted) ?? throw Invalid(pattern);
        }
        catch (JsonException)
        {
            throw Invalid(pattern);
        }
    }

    private static FormatException Invalid(string pattern) => new($"Invalid JSON path pattern: {pattern}");

    private static bool MatchTokens(Token[] tokens, int ti, IReadOnlyList<JsonPathSegment> segs, int si)
    {
        if (ti == tokens.Length) return si == segs.Count;
        var t = tokens[ti];
        if (t.Kind == TokenKind.Deep)
        {
            for (var k = si; k <= segs.Count; k++)
                if (MatchTokens(tokens, ti + 1, segs, k)) return true;
            return false;
        }
        if (si == segs.Count) return false;
        var s = segs[si];
        var ok = t.Kind switch
        {
            TokenKind.Any => true,
            TokenKind.AnyIndex => s.IsIndex,
            TokenKind.Index => s.IsIndex && s.Index == t.Index,
            TokenKind.Key => !s.IsIndex && s.Key == t.Key,
            _ => false,
        };
        return ok && MatchTokens(tokens, ti + 1, segs, si + 1);
    }

    /// <summary>The web's <c>/^[A-Za-z_$][A-Za-z0-9_$]*$/</c>.</summary>
    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0) return false;
        if (!(char.IsAsciiLetter(s[0]) || s[0] == '_' || s[0] == '$')) return false;
        for (var i = 1; i < s.Length; i++)
            if (!(char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) return false;
        return true;
    }

    /// <summary>
    /// <c>JSON.stringify</c> of a string: only <c>"</c>, <c>\</c>, control characters and lone surrogates are
    /// escaped; every other character, non-ASCII included, is kept as is. (System.Text.Json escapes far more by
    /// default, which would give a different path.)
    /// </summary>
    internal static string JsString(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); }
                    else if (char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
