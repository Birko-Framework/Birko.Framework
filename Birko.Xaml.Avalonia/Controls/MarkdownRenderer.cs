using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Minimal, dependency-free Markdown → Avalonia renderer for the <see cref="MarkdownEditor"/> preview.
/// Supports a common subset: ATX headings, unordered lists, fenced code blocks, horizontal rules,
/// paragraphs, and inline <c>**bold**</c> / <c>*italic*</c> / <c>`code`</c> / <c>[text](url)</c>.
/// Block foregrounds use design tokens (re-theme live); swap in Markdig later for full CommonMark.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly double[] HeadingSizes = { 26, 22, 18, 16, 14, 13 };

    public static Control Render(string markdown)
    {
        var root = new StackPanel { Spacing = 8 };
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // Fenced code block
            if (line.TrimStart().StartsWith("```"))
            {
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```")) code.Add(lines[i++]);
                root.Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            // Horizontal rule
            if (Regex.IsMatch(line.Trim(), @"^(-{3,}|\*{3,})$"))
            {
                var hr = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
                hr.Bind(Border.BackgroundProperty, hr.GetResourceObservable("BBorderBrush"));
                root.Children.Add(hr);
                continue;
            }

            // Heading
            var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (h.Success)
            {
                int level = h.Groups[1].Value.Length;
                var tb = Block(h.Groups[2].Value, "BTextBrush");
                tb.FontSize = HeadingSizes[level - 1];
                tb.FontWeight = FontWeight.SemiBold;
                root.Children.Add(tb);
                continue;
            }

            // Unordered list (group consecutive items)
            if (Regex.IsMatch(line, @"^\s*[-*]\s+"))
            {
                var list = new StackPanel { Spacing = 2, Margin = new Thickness(4, 0, 0, 0) };
                while (i < lines.Length && Regex.IsMatch(lines[i], @"^\s*[-*]\s+"))
                {
                    string item = Regex.Replace(lines[i], @"^\s*[-*]\s+", "");
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    var bullet = new TextBlock { Text = "•" };
                    bullet.Bind(TextBlock.ForegroundProperty, bullet.GetResourceObservable("BTextSecondaryBrush"));
                    row.Children.Add(bullet);
                    row.Children.Add(Block(item, "BTextBrush"));
                    list.Children.Add(row);
                    i++;
                }
                i--; // the for-loop will re-increment
                root.Children.Add(list);
                continue;
            }

            // Paragraph (join consecutive plain lines)
            var para = new List<string> { line };
            while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1])
                   && !IsBlockStart(lines[i + 1]))
                para.Add(lines[++i]);
            root.Children.Add(Block(string.Join(" ", para), "BTextBrush"));
        }

        return root;
    }

    private static bool IsBlockStart(string line) =>
        line.TrimStart().StartsWith("```")
        || Regex.IsMatch(line, @"^#{1,6}\s+")
        || Regex.IsMatch(line, @"^\s*[-*]\s+")
        || Regex.IsMatch(line.Trim(), @"^(-{3,}|\*{3,})$");

    private static Border CodeBlock(string code)
    {
        var tb = new TextBlock { Text = code, TextWrapping = TextWrapping.Wrap };
        tb.Bind(TextBlock.FontFamilyProperty, tb.GetResourceObservable("BFontMono"));
        tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable("BTextBrush"));
        var border = new Border { Padding = new Thickness(12), Child = tb };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("BBgTertiaryBrush"));
        border.Bind(Border.CornerRadiusProperty, border.GetResourceObservable("BRadius"));
        return border;
    }

    private static TextBlock Block(string text, string tokenKey)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable(tokenKey));
        foreach (var inline in ParseInlines(text)) tb.Inlines!.Add(inline);
        return tb;
    }

    // Inline: **bold**, *italic*, `code`, [text](url). Everything else is plain text.
    private static readonly Regex InlineRx = new(
        @"(\*\*(?<b>.+?)\*\*)|(\*(?<i>.+?)\*)|(`(?<c>.+?)`)|(\[(?<lt>.+?)\]\((?<lu>.+?)\))",
        RegexOptions.Compiled);

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        int pos = 0;
        foreach (Match m in InlineRx.Matches(text))
        {
            if (m.Index > pos) yield return new Run(text.Substring(pos, m.Index - pos));
            if (m.Groups["b"].Success) yield return new Run(m.Groups["b"].Value) { FontWeight = FontWeight.Bold };
            else if (m.Groups["i"].Success) yield return new Run(m.Groups["i"].Value) { FontStyle = FontStyle.Italic };
            else if (m.Groups["c"].Success) yield return new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Cascadia Code, Consolas, monospace") };
            else if (m.Groups["lt"].Success) yield return new Run(m.Groups["lt"].Value) { FontWeight = FontWeight.SemiBold };
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) yield return new Run(text.Substring(pos));
    }
}
