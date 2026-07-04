using System.Collections.Specialized;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// A CRUD list VM with client-side search. Set <see cref="SearchMatch"/> to a predicate; bind the
/// list to <see cref="Filtered"/>, which recomputes when <see cref="SearchText"/> or the underlying
/// items change.
/// </summary>
public partial class ListPageViewModel<T> : CrudViewModelBase<T>
    where T : class
{
    public ListPageViewModel(ICrudDataSource<T> data, II18n? i18n = null) : base(data, i18n)
        => Items.CollectionChanged += OnItemsChanged;

    [ObservableProperty]
    private string? _searchText;

    /// <summary>Predicate deciding whether an item matches the current search text. Null = no filtering.</summary>
    public Func<T, string, bool>? SearchMatch { get; set; }

    /// <summary>The items after applying <see cref="SearchText"/> via <see cref="SearchMatch"/>.</summary>
    public IEnumerable<T> Filtered =>
        SearchMatch is null || string.IsNullOrWhiteSpace(SearchText)
            ? Items
            : Items.Where(i => SearchMatch(i, SearchText!));

    partial void OnSearchTextChanged(string? value) => OnPropertyChanged(nameof(Filtered));

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(Filtered));
}
