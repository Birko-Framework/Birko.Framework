using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Mvvm;
using Birko.Xaml.Core.Navigation;
using Birko.Xaml.Shell.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

// File-local data source over the public Contact model (the port a real app adapts its store to).
file sealed class MobileContacts : ICrudDataSource<Contact>
{
    private readonly List<Contact> _items = new() { new Contact { Name = "Ada" }, new Contact { Name = "Grace" } };
    public Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Contact>>(_items);
    public Task<Contact?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Contact?>(_items.FirstOrDefault());
    public Task<Guid> SaveAsync(Contact item, CancellationToken ct = default) { if (!_items.Contains(item)) _items.Add(item); return Task.FromResult(Guid.NewGuid()); }
    public Task DeleteAsync(Contact item, CancellationToken ct = default) { _items.Remove(item); return Task.CompletedTask; }
    public Contact NewInstance() => new();
}

public class MobileShellRenderTests
{
    private static ShellViewModel BuildShell()
    {
        var data = new MobileContacts();
        var nav = new NavigationService().Register(
            new ModuleDefinition { Id = "home", Label = "Home", Icon = "🏠", CreateViewModel = () => { var vm = new ListPageViewModel<Contact>(data); vm.LoadAsync(); return vm; } },
            new ModuleDefinition { Id = "log", Label = "Log", Icon = "➕", CreateViewModel = () => new ListPageViewModel<Contact>(data) },
            new ModuleDefinition { Id = "stats", Label = "Stats", Icon = "📊", CreateViewModel = () => new ListPageViewModel<Contact>(data) });
        var shell = new ShellViewModel(nav, new AvaloniaThemeManager()) { Title = "Reps" };
        nav.Navigate("home");
        return shell;
    }

    private static Window ShowMobile(ShellViewModel shell, out MobileShellView view)
    {
        view = new MobileShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 390, Height = 780 };
        window.Show();
        window.Measure(new global::Avalonia.Size(390, 780));
        window.Arrange(new global::Avalonia.Rect(0, 0, 390, 780));
        return window;
    }

    [AvaloniaFact]
    public void Bottom_nav_has_one_item_per_surface()
    {
        var shell = BuildShell();
        ShowMobile(shell, out var view);

        var navButtons = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("navitem")).ToList();
        navButtons.Should().HaveCount(3, "one bottom-nav item per registered module");
    }

    [AvaloniaFact]
    public void Renders_the_active_page_via_the_view_locator()
    {
        var shell = BuildShell();
        ShowMobile(shell, out var view);

        view.GetVisualDescendants().OfType<ListPageView>().Should()
            .ContainSingle("the active surface's page renders in the content region");
    }

    [AvaloniaFact]
    public void Bottom_nav_switches_the_active_surface()
    {
        var shell = BuildShell();
        ShowMobile(shell, out _);

        shell.Nav.CurrentModule!.Id.Should().Be("home");
        shell.NavigateCommand.Execute("stats");
        shell.Nav.CurrentModule!.Id.Should().Be("stats");
        shell.NavItems.Single(i => i.Id == "stats").IsActive.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Capture_mobile_shell_screenshot()
    {
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var shell = BuildShell();
        var window = ShowMobile(shell, out var view);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "mobile-shell.png"));
        view.IsInitialized.Should().BeTrue();
    }
}
