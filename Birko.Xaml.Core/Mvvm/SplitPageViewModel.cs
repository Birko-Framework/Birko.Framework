using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// Master–detail page VM (the analogue of Birko.Web's <c>BaseSplitPage</c>): the master is the
/// searchable list from <see cref="ListPageViewModel{T}"/>; the detail pane binds a <c>Form</c> (using
/// the inherited <see cref="CrudViewModelBase{T}.Fields"/> schema) to the current
/// <see cref="CrudViewModelBase{T}.SelectedItem"/>. The Avalonia <c>SplitPageView</c> lays this out
/// over a <c>SplitPanel</c>.
/// </summary>
public class SplitPageViewModel<T> : ListPageViewModel<T>
    where T : class
{
    public SplitPageViewModel(ICrudDataSource<T> data, II18n? i18n = null) : base(data, i18n) { }
}
