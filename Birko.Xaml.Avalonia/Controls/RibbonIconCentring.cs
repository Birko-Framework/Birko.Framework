using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Optical centring of a command's icon glyph (seen by the user in the Gallery, 2026-10-23): an icon is a character, and
/// characters from different fonts sit at different heights in the same line box. A math letter such as 𝐁 or 𝑰 came from
/// a fallback font whose metrics put its ink about 2px below the box's middle, so it read as lower than its label,
/// though the boxes were centred alike. The glyph's ink is measured and the icon is shifted by what puts the ink's middle
/// on the box's; layout is untouched, so no command changes size.
/// </summary>
public partial class Ribbon
{
    private static T CentreInk<T>(T icon) where T : TextBlock
    {
        (string? Text, double Size, double Height) measured = default;
        icon.LayoutUpdated += (_, _) =>
        {
            var now = (icon.Text, icon.FontSize, icon.Bounds.Height);
            if (now == measured || string.IsNullOrEmpty(icon.Text) || icon.Bounds.Height <= 0) return;
            measured = now;
            double shift = InkShift(icon);
            icon.RenderTransform = shift == 0 ? null : new TranslateTransform(0, shift);
        };
        return icon;
    }

    /// <summary>How far down to move the icon so its ink is centred in its box; 0 when it cannot be measured.</summary>
    internal static double InkShift(TextBlock icon)
    {
        if (icon.TextLayout is not { TextLines.Count: > 0 } layout) return 0;
        var typeface = new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch);
        var text = new FormattedText(icon.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, icon.FontSize, Brushes.Black);
        if (text.BuildGeometry(default) is not { } ink || ink.Bounds.Height <= 0) return 0;
        // The ink relative to the baseline, placed on the baseline the TextBlock draws at.
        double baseline = layout.TextLines[0].Baseline;
        double top = baseline + (ink.Bounds.Top - text.Baseline);
        double inkMiddle = top + ink.Bounds.Height / 2;
        return System.Math.Round(icon.Bounds.Height / 2 - inkMiddle);
    }
}
