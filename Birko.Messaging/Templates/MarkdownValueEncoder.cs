using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Messaging.Templates;

/// <summary>
/// Makes a merged value render as literal text in Markdown: every ASCII punctuation and symbol character is
/// backslash-escaped, which CommonMark (§2.4) allows for all of them. Emphasis, links, autolinks (the escaped
/// <c>:</c> and <c>.</c> stop <c>https://</c> and <c>www.</c> from matching), images, headings, lists, quotes, tables
/// and entities in the value all stay text.
/// </summary>
/// <remarks>
/// <para>Line structure is the one thing escaping cannot neutralise, so it is normalised: leading spaces and tabs are
/// removed from the value and from each of its lines (indentation after a blank line would start a code block), and
/// blank lines are dropped (one would end the paragraph or list item the placeholder sits in). Line breaks are kept.</para>
/// <para>Backslashes are not processed inside a code span or block, so a placeholder placed there shows them. A value that
/// must stay live, such as a link the system built, belongs in <see cref="TemplateRenderOptions.RawPaths"/>.</para>
/// </remarks>
public sealed partial class MarkdownValueEncoder : ITemplateValueEncoder
{
    [GeneratedRegex(@"(\r?\n)[ \t]+")]
    private static partial Regex IndentAfterBreak();

    [GeneratedRegex(@"(\r?\n)(?:\r?\n)+")]
    private static partial Regex BlankLines();

    public string Encode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var lines = BlankLines().Replace(IndentAfterBreak().Replace(value.TrimStart(' ', '\t'), "$1"), "$1");
        var builder = new StringBuilder(lines.Length * 2);
        foreach (var c in lines)
        {
            if (c < 128 && (char.IsPunctuation(c) || char.IsSymbol(c)))
            {
                builder.Append('\\');
            }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
