using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Birko.Xaml.Core.Kanban;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Kanban board (the XAML port of <c>b-kanban</c>): horizontal token-styled columns, each an
/// observable list of card surfaces. Cards can be dragged between columns (best-effort pointer
/// drag-drop); moving a card in the model (<see cref="KanbanColumn.Cards"/>) also updates the board
/// live. Recursive card nesting is out of scope for this first cut.
/// </summary>
public class Kanban : ContentControl
{
    public static readonly StyledProperty<IEnumerable<KanbanColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<Kanban, IEnumerable<KanbanColumn>?>(nameof(Columns));

    private const string CardFormat = "birko-kanban-card";

    static Kanban() => ColumnsProperty.Changed.AddClassHandler<Kanban>((k, _) => k.Rebuild());

    public IEnumerable<KanbanColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private void Rebuild()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (Columns is not null)
            foreach (var column in Columns)
                row.Children.Add(BuildColumn(column));

        Content = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private Control BuildColumn(KanbanColumn column)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var title = new TextBlock { Text = column.Title };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("BTextBrush"));
        title.FontWeight = global::Avalonia.Media.FontWeight.SemiBold;
        header.Children.Add(title);
        var count = new TextBlock { Text = column.Cards.Count.ToString(), VerticalAlignment = VerticalAlignment.Center };
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("BTextMutedBrush"));
        column.Cards.CollectionChanged += (_, _) => count.Text = column.Cards.Count.ToString();
        header.Children.Add(count);

        var list = new ItemsControl { ItemsSource = column.Cards, ItemTemplate = CardTemplate() };

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(list);

        var border = new Border
        {
            Width = 260,
            Padding = new Thickness(12),
            Child = dock,
            Tag = column,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("BBgSecondaryBrush"));
        border.Bind(Border.CornerRadiusProperty, border.GetResourceObservable("BRadiusLg"));

        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(column, e));
        return border;
    }

    private FuncDataTemplate<KanbanCard> CardTemplate() => new((card, _) =>
    {
        var stack = new StackPanel { Spacing = 2 };
        var title = new TextBlock { Text = card.Title, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("BTextBrush"));
        stack.Children.Add(title);
        if (!string.IsNullOrEmpty(card.Description))
        {
            var desc = new TextBlock { Text = card.Description, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
            desc.Bind(TextBlock.ForegroundProperty, desc.GetResourceObservable("BTextSecondaryBrush"));
            stack.Children.Add(desc);
        }

        var border = new Border
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("BBgElevatedBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("BBorderBrush"));
        border.Bind(Border.CornerRadiusProperty, border.GetResourceObservable("BRadius"));

        border.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            var data = new DataObject();
            data.Set(CardFormat, card);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        };
        return border;
    });

    private void OnDrop(KanbanColumn target, DragEventArgs e)
    {
        if (e.Data.Get(CardFormat) is not KanbanCard card || Columns is null) return;
        var source = Columns.FirstOrDefault(c => c.Cards.Contains(card));
        if (source is null || source == target) return;
        source.Cards.Remove(card);
        target.Cards.Add(card);
        e.DragEffects = DragDropEffects.Move;
    }
}
