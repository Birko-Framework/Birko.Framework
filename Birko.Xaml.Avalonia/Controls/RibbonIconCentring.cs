using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Optical alignment of a command's icon glyph (seen by the user in the Gallery, 2026-10-23). An icon is a character, and
/// characters from different fonts sit at different heights in the same line box: Bold's 𝐁 and Italic's 𝑰 came from a
/// math font that drew them about 2px low, though the boxes were centred alike. So the icon is shifted, at draw time
/// only (no size changes), to where the eye expects it beside its label:
/// <list type="bullet">
/// <item>a LETTER icon (𝐁, 𝑰, A) stands on the label's baseline, as text beside text: centred, its foot sat below
/// "Bold"'s baseline, still 1–2px off at the user's second check;</item>
/// <item>a picture icon (✂, 📋, ▥) has its measured ink centred on the label's capital letters (baseline minus half the
/// cap height);</item>
/// <item>an icon with no label beside it (Large, Small, the toolbar) has its ink centred in its box.</item>
/// </list>
/// Positions come from the layout, never through the icon's own transform, and the shift is rounded to device pixels so
/// the glyph stays sharp.
/// </summary>
public partial class Ribbon
{
    private static T CentreInk<T>(T icon) where T : TextBlock
    {
        double applied = double.NaN;
        icon.LayoutUpdated += (_, _) =>
        {
            if (string.IsNullOrEmpty(icon.Text) || icon.Bounds.Height <= 0) return;
            double shift = InkShift(icon);
            if (shift == applied) return;
            applied = shift;
            icon.RenderTransform = shift == 0 ? null : new TranslateTransform(0, shift);
        };
        return icon;
    }

    /// <summary>Lines <paramref name="icon"/> up with <paramref name="label"/> beside it.</summary>
    private static void AlignInkWith(TextBlock icon, TextBlock label) => icon.Tag = label;

    /// <summary>How far down to draw the icon; 0 when it cannot be measured.</summary>
    internal static double InkShift(TextBlock icon)
    {
        if (icon.TextLayout is not { TextLines.Count: > 0 } layout) return 0;
        double iconBaseline = layout.TextLines[0].Baseline;
        double shift;
        if (icon.Tag is TextBlock { TextLayout.TextLines.Count: > 0 } label && label.Bounds.Height > 0
            && LabelBaseline(icon, label) is { } labelBaseline)
        {
            if (IsLetter(icon.Text!)) shift = labelBaseline - iconBaseline;
            else if (InkMiddle(icon) is { } middle && CapHeight(label) is { } cap) shift = labelBaseline - cap / 2 - middle;
            else return 0;
        }
        else if (InkMiddle(icon) is { } middle) shift = icon.Bounds.Height / 2 - middle;
        else return 0;
        double scale = (icon.GetVisualRoot() as TopLevel)?.RenderScaling ?? 1;
        return System.Math.Round(shift * scale) / scale;
    }

    /// <summary>The label's baseline in the icon's layout coordinates (both in the same panel, by their layout bounds).</summary>
    private static double? LabelBaseline(TextBlock icon, TextBlock label)
    {
        if (icon.GetVisualParent() is not Visual parent) return null;
        double labelTop = ReferenceEquals(label.GetVisualParent(), parent)
            ? label.Bounds.Y
            : label.TranslatePoint(default, parent) is { } p ? p.Y : double.NaN;
        if (double.IsNaN(labelTop)) return null;
        return labelTop + label.TextLayout.TextLines[0].Baseline - icon.Bounds.Y;
    }

    private static bool IsLetter(string text) =>
        text.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(text, 0) is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter;

    private static double? CapHeight(TextBlock label)
    {
        var face = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
        var cap = new FormattedText("H", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, label.FontSize, Brushes.Black);
        return cap.BuildGeometry(default) is { Bounds.Height: > 0 } ink ? ink.Bounds.Height : null;
    }

    /// <summary>The icon's ink middle in its own layout coordinates; null when it cannot be measured.</summary>
    internal static double? InkMiddle(TextBlock icon)
    {
        if (icon.TextLayout is not { TextLines.Count: > 0 } layout) return null;
        var typeface = new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch);
        var text = new FormattedText(icon.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, icon.FontSize, Brushes.Black);
        if (text.BuildGeometry(default) is not { } ink || ink.Bounds.Height <= 0) return null;
        // The ink relative to the baseline, placed on the baseline the TextBlock draws at.
        double top = layout.TextLines[0].Baseline + (ink.Bounds.Top - text.Baseline);
        return top + ink.Bounds.Height / 2;
    }
}
