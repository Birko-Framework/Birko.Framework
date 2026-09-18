using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Split Markdown editor (the XAML port of <c>b-markdown-editor</c>): a monospaced editing pane on
/// the left and a live token-styled preview on the right (rendered by <see cref="MarkdownRenderer"/>).
/// Bind <see cref="Markdown"/> two-way.
/// </summary>
public class MarkdownEditor : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownEditor, string?>(nameof(Markdown), defaultBindingMode: BindingMode.TwoWay);

    private readonly ContentControl _preview = new();

    static MarkdownEditor() => MarkdownProperty.Changed.AddClassHandler<MarkdownEditor>((e, _) => e.UpdatePreview());

    public MarkdownEditor()
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        editor.Bind(TextBox.FontFamilyProperty, editor.GetResourceObservable("BFontMono"));
        editor.Bind(TextBox.TextProperty, new Binding(nameof(Markdown)) { Source = this, Mode = BindingMode.TwoWay });

        var splitter = new GridSplitter { Width = 4 };
        splitter.Bind(GridSplitter.BackgroundProperty, splitter.GetResourceObservable("BBorderBrush"));

        var previewScroll = new ScrollViewer { Content = _preview, Padding = new Thickness(12, 0, 0, 0) };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*") };
        Grid.SetColumn(editor, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(previewScroll, 2);
        grid.Children.Add(editor);
        grid.Children.Add(splitter);
        grid.Children.Add(previewScroll);

        Content = grid;
        UpdatePreview();
    }

    /// <summary>The markdown source (two-way; edited in the left pane, rendered in the right).</summary>
    public string? Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    private void UpdatePreview() => _preview.Content = MarkdownRenderer.Render(Markdown ?? string.Empty);
}
