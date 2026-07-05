using System.Collections.ObjectModel;

namespace Birko.Xaml.Core.Kanban;

/// <summary>One card on a kanban board (the XAML analogue of <c>b-kanban</c> cards).</summary>
public sealed class KanbanCard
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
}

/// <summary>A kanban column holding an observable list of <see cref="KanbanCard"/> (so moving a card
/// between columns updates the board live).</summary>
public sealed class KanbanColumn
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public ObservableCollection<KanbanCard> Cards { get; } = new();
}
