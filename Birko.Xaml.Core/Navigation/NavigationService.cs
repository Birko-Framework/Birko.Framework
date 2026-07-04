using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Core.Navigation;

/// <summary>Default <see cref="INavigationService"/>: a registered module map, a back-history stack,
/// and the active page VM. Avalonia-free.</summary>
public sealed partial class NavigationService : ObservableObject, INavigationService
{
    private readonly List<ModuleDefinition> _modules = new();
    private readonly Stack<string> _history = new();

    public IReadOnlyList<ModuleDefinition> Modules => _modules;

    [ObservableProperty]
    private object? _current;

    [ObservableProperty]
    private ModuleDefinition? _currentModule;

    public bool CanGoBack => _history.Count > 1;

    public event EventHandler? Navigated;

    /// <summary>Register the modules (nav order preserved). Returns this for chaining.</summary>
    public NavigationService Register(params ModuleDefinition[] modules)
    {
        _modules.AddRange(modules);
        OnPropertyChanged(nameof(Modules));
        return this;
    }

    public bool Navigate(string moduleId)
    {
        var module = _modules.FirstOrDefault(m => m.Id == moduleId);
        if (module is null) return false;

        _history.Push(moduleId);
        Activate(module);
        return true;
    }

    public void Back()
    {
        if (!CanGoBack) return;
        _history.Pop();                       // drop current
        var previous = _history.Peek();
        var module = _modules.First(m => m.Id == previous);
        Activate(module);
    }

    private void Activate(ModuleDefinition module)
    {
        CurrentModule = module;
        Current = module.CreateViewModel();
        OnPropertyChanged(nameof(CanGoBack));
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
