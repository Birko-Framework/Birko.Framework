using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Forms;
using Birko.Xaml.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// Detail/form VM: load one entity by id, bind it as <see cref="Model"/>, and Save/Cancel.
/// Save is permission-gated (<see cref="CanSave"/>) and requires a loaded model.
/// </summary>
public abstract partial class DetailPageViewModel<T> : BasePageViewModel
    where T : class
{
    private readonly ICrudDataSource<T> _data;

    protected DetailPageViewModel(ICrudDataSource<T> data, II18n? i18n = null) : base(i18n)
        => _data = data;

    [ObservableProperty]
    private T? _model;

    [ObservableProperty]
    private bool _canSave = true;

    /// <summary>Field schema for the detail form (bound to <see cref="Model"/> by the view).</summary>
    public IEnumerable<FormField>? Fields { get; set; }

    /// <summary>Load an existing entity by id, or start a new one when <paramref name="id"/> is null.</summary>
    public async Task LoadAsync(Guid? id, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Model = id is { } g ? await _data.GetAsync(g, ct) : _data.NewInstance();
            IsLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveModel))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (Model is null) return;
        await _data.SaveAsync(Model, ct);
    }

    private bool CanSaveModel() => CanSave && Model is not null;

    [RelayCommand]
    private void Cancel() => Model = null;

    partial void OnCanSaveChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnModelChanged(T? value) => SaveCommand.NotifyCanExecuteChanged();
}
