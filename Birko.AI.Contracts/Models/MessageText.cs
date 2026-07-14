using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.AI.Models
{
    /// <summary>
    /// Canonical extractor for the plain text of a <see cref="Message.Content"/> value.
    /// <see cref="Message.Content"/> is <c>object?</c> — a <see cref="string"/> for user turns but a
    /// <c>List&lt;ContentBlock&gt;</c> for assistant turns — so a naive <c>Content is string</c> read
    /// gets a block list's <c>ToString()</c> (a CLR type name) on assistant turns. This is THE way to
    /// read a message's text regardless of which shape the content is in.
    /// </summary>
    public static class MessageText
    {
        /// <summary>
        /// Returns the text of a message content value: the string itself for string content, or the
        /// concatenated <see cref="ContentBlock.Text"/> of every <c>type == "text"</c> block for a
        /// block list (or a single block). Null/empty/unrecognized content yields
        /// <see cref="string.Empty"/>.
        /// </summary>
        public static string From(object? content)
        {
            switch (content)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
                case ContentBlock block:
                    return block.Type == "text" ? (block.Text ?? string.Empty) : string.Empty;
                case IEnumerable<ContentBlock> blocks:
                    {
                        var sb = new StringBuilder();
                        foreach (var b in blocks.Where(b => b != null && b.Type == "text" && b.Text != null))
                        {
                            sb.Append(b.Text);
                        }
                        return sb.ToString();
                    }
                default:
                    return string.Empty;
            }
        }
    }
}
