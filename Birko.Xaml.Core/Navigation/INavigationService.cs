using System.ComponentModel;

namespace Birko.Xaml.Core.Navigation;

/// <summary>
/// Platform-neutral navigation over a module/route map (the re-architected analogue of Birko.Web's
/// hash router). The Avalonia shell binds a content region to <see cref="Current"/> (through a
/// ViewLocator) and the nav list to <see cref="Modules"/>.
/// </summary>
public interface INavigationService : INotifyPropertyChanged
{
    /// <summary>The registered modules, in nav order.</summary>
    IReadOnlyList<ModuleDefinition> Modules { get; }

    /// <summary>The active page ViewModel (what the content region shows).</summary>
    object? Current { get; }

    /// <summary>The module the <see cref="Current"/> page came from.</summary>
    ModuleDefinition? CurrentModule { get; }

    /// <summary>True when there is history to <see cref="Back"/> to.</summary>
    bool CanGoBack { get; }

    /// <summary>Navigate to a module by id (builds a fresh page VM). No-op for an unknown id.</summary>
    /// <returns><c>true</c> if the module existed and was activated.</returns>
    bool Navigate(string moduleId);

    /// <summary>Return to the previous page in history, if any.</summary>
    void Back();

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    event EventHandler? Navigated;
}
