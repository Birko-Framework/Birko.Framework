using System.Collections.ObjectModel;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// Base CRUD ViewModel: an observable item collection, selection, an editing slot, and
/// permission-gated Create / Edit / Delete / Refresh / Save commands. UI-agnostic — a view binds
/// the collection, selection and <see cref="EditingItem"/>; the commands' <c>CanExecute</c> honors
/// the permission flags and selection so buttons enable/disable automatically.
/// </summary>
public abstract partial class CrudViewModelBase<T> : BasePageViewModel
    where T : class
{
    private readonly ICrudDataSource<T> _data;

    protected CrudViewModelBase(ICrudDataSource<T> data, II18n? i18n = null) : base(i18n)
        => _data = data;

    /// <summary>The loaded entities.</summary>
    public ObservableCollection<T> Items { get; } = new();

    [ObservableProperty]
    private T? _selectedItem;

    /// <summary>The entity currently being created/edited (bound by a detail form). Null when not editing.</summary>
    [ObservableProperty]
    private T? _editingItem;

    [ObservableProperty]
    private bool _canCreate = true;

    [ObservableProperty]
    private bool _canEdit = true;

    [ObservableProperty]
    private bool _canDelete = true;

    /// <summary>Load = refresh the collection.</summary>
    public override Task LoadAsync(CancellationToken ct = default) => RefreshAsync(ct);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            foreach (var item in await _data.GetAllAsync(ct))
                Items.Add(item);
            IsLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => EditingItem = _data.NewInstance();

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void Edit() => EditingItem = SelectedItem;

    private bool CanEditSelected() => CanEdit && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteAsync(CancellationToken ct = default)
    {
        if (SelectedItem is null) return;
        await _data.DeleteAsync(SelectedItem, ct);
        Items.Remove(SelectedItem);
        SelectedItem = null;
    }

    private bool CanDeleteSelected() => CanDelete && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanSaveEditing))]
    private async Task SaveEditingAsync(CancellationToken ct = default)
    {
        if (EditingItem is null) return;
        await _data.SaveAsync(EditingItem, ct);
        EditingItem = null;
        await RefreshAsync(ct);
    }

    private bool CanSaveEditing() => EditingItem is not null;

    [RelayCommand]
    private void CancelEdit() => EditingItem = null;

    // Keep command CanExecute in sync as flags/selection/editing change.
    partial void OnCanCreateChanged(bool value) => CreateCommand.NotifyCanExecuteChanged();

    partial void OnCanEditChanged(bool value) => EditCommand.NotifyCanExecuteChanged();

    partial void OnCanDeleteChanged(bool value) => DeleteCommand.NotifyCanExecuteChanged();

    partial void OnSelectedItemChanged(T? value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditingItemChanged(T? value) => SaveEditingCommand.NotifyCanExecuteChanged();
}
