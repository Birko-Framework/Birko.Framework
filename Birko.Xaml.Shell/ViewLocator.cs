using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Birko.Xaml.Core.Mvvm;
using Birko.Xaml.Shell.Views;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Shell;

/// <summary>
/// Resolves a ViewModel to its View (the analogue of Birko.Web's view resolution). Tries a
/// <c>*ViewModel → *View</c> naming convention first (for custom pages), then falls back to the
/// generic page-base views for <c>Split/List/DetailPageViewModel&lt;T&gt;</c>. Add it to
/// <c>Application.DataTemplates</c> (or a content region's template) so bound VMs render.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "(null)" };

        // 1) Naming convention: {ns}.FooViewModel -> {ns}.FooView.
        var vmType = data.GetType();
        if (vmType.FullName is { } full && full.Contains("ViewModel"))
        {
            var viewName = full.Replace("ViewModel", "View");
            var viewType = vmType.Assembly.GetType(viewName) ?? Type.GetType(viewName);
            if (viewType is not null && typeof(Control).IsAssignableFrom(viewType))
                return (Control)Activator.CreateInstance(viewType)!;
        }

        // 2) Generic page bases have no 1:1 named view — map by base type (Split before List).
        if (DerivesFromGeneric(vmType, typeof(SplitPageViewModel<>))) return new SplitPageView();
        if (DerivesFromGeneric(vmType, typeof(ListPageViewModel<>))) return new ListPageView();
        if (DerivesFromGeneric(vmType, typeof(DetailPageViewModel<>))) return new DetailPageView();

        return new TextBlock { Text = $"No view for {vmType.Name}" };
    }

    public bool Match(object? data) => data is ObservableObject;

    private static bool DerivesFromGeneric(Type type, Type openGeneric)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == openGeneric)
                return true;
        return false;
    }
}
