using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Forms;
using Birko.Xaml.Core.Mvvm;
using Birko.Xaml.Core.Navigation;
using Birko.Xaml.Shell;
using Birko.Xaml.Shell.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

// A tiny in-memory data source (the port a real app adapts its Birko.Data store to).
file sealed class Contacts : ICrudDataSource<Contact>
{
    private readonly List<Contact> _items = new()
    {
        new Contact { Name = "Ada" }, new Contact { Name = "Grace" }, new Contact { Name = "Linus" },
    };
    public Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Contact>>(_items);
    public Task<Contact?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Contact?>(_items.FirstOrDefault());
    public Task<Guid> SaveAsync(Contact item, CancellationToken ct = default) { if (!_items.Contains(item)) _items.Add(item); return Task.FromResult(Guid.NewGuid()); }
    public Task DeleteAsync(Contact item, CancellationToken ct = default) { _items.Remove(item); return Task.CompletedTask; }
    public Contact NewInstance() => new();
}

public class NavigationTests
{
    private static (NavigationService nav, int aBuilt, int bBuilt) Setup()
    {
        int a = 0, b = 0;
        var nav = new NavigationService().Register(
            new ModuleDefinition { Id = "a", Label = "Alpha", CreateViewModel = () => { a++; return new object(); } },
            new ModuleDefinition { Id = "b", Label = "Beta", CreateViewModel = () => { b++; return new object(); } });
        return (nav, a, b);
    }

    [Fact]
    public void Navigate_sets_current_and_module()
    {
        var (nav, _, _) = Setup();
        nav.Navigate("a").Should().BeTrue();
        nav.Current.Should().NotBeNull();
        nav.CurrentModule!.Id.Should().Be("a");
    }

    [Fact]
    public void Unknown_module_is_ignored()
    {
        var (nav, _, _) = Setup();
        nav.Navigate("nope").Should().BeFalse();
        nav.Current.Should().BeNull();
    }

    [Fact]
    public void Back_returns_to_previous()
    {
        var (nav, _, _) = Setup();
        nav.Navigate("a");
        nav.Navigate("b");
        nav.CanGoBack.Should().BeTrue();
        nav.Back();
        nav.CurrentModule!.Id.Should().Be("a");
    }
}

public class ViewLocatorTests
{
    private static readonly ViewLocator Locator = new();

    [AvaloniaFact]
    public void Maps_split_list_detail_page_vms_to_generic_views()
    {
        var data = new Contacts();
        Locator.Build(new SplitPageViewModel<Contact>(data)).Should().BeOfType<SplitPageView>();
        Locator.Build(new ListPageViewModel<Contact>(data)).Should().BeOfType<ListPageView>();
        Locator.Build(new TestDetailVm(data)).Should().BeOfType<DetailPageView>();
    }

    private sealed class TestDetailVm : DetailPageViewModel<Contact>
    {
        public TestDetailVm(ICrudDataSource<Contact> d) : base(d) { }
    }
}

public class ShellRenderTests
{
    private static ShellViewModel BuildShell()
    {
        var data = new Contacts();
        var fields = new[] { new FormField { Name = nameof(Contact.Name), Label = "Name", Required = true } };
        var nav = new NavigationService().Register(
            new ModuleDefinition
            {
                Id = "contacts", Label = "Contacts",
                CreateViewModel = () => { var vm = new SplitPageViewModel<Contact>(data) { Fields = fields }; vm.LoadAsync(); return vm; },
            },
            new ModuleDefinition { Id = "about", Label = "About", CreateViewModel = () => new ListPageViewModel<Contact>(data) });
        var shell = new ShellViewModel(nav, new AvaloniaThemeManager())
        {
            Title = "Demo",
            UserName = "Ada Lovelace",
            UserCommands = new[]
            {
                new Birko.Xaml.Core.Commands.CommandItem { Id = "profile", Label = "Profile" },
                new Birko.Xaml.Core.Commands.CommandItem { Id = "signout", Label = "Sign out" },
            },
            Tenants = new[] { "Acme Inc.", "Globex" },
            CurrentTenant = "Acme Inc.",
        };
        nav.Navigate("contacts");
        return shell;
    }

    [AvaloniaFact]
    public void Shell_renders_the_active_page_via_the_view_locator()
    {
        var shell = BuildShell();
        var view = new ShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        window.Measure(new global::Avalonia.Size(900, 600));
        window.Arrange(new global::Avalonia.Rect(0, 0, 900, 600));

        // The content region resolved the SplitPageViewModel to a SplitPageView.
        view.GetVisualDescendants().OfType<SplitPageView>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Shell_navigation_swaps_the_page()
    {
        var shell = BuildShell();
        shell.Nav.CurrentModule!.Id.Should().Be("contacts");
        shell.NavigateCommand.Execute("about");
        shell.Nav.CurrentModule!.Id.Should().Be("about");
        shell.Nav.Current.Should().BeOfType<ListPageViewModel<Contact>>();
    }

    [AvaloniaFact]
    public void Capture_shell_screenshot()
    {
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var shell = BuildShell();
        var view = new ShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        window.Measure(new global::Avalonia.Size(900, 600));
        window.Arrange(new global::Avalonia.Rect(0, 0, 900, 600));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "shell.png"));
        view.IsInitialized.Should().BeTrue();
    }
}

public class ShellChromeTests
{
    private static ShellViewModel Shell()
    {
        var data = new Contacts();
        var nav = new NavigationService().Register(
            new ModuleDefinition { Id = "contacts", Label = "Contacts", CreateViewModel = () => new ListPageViewModel<Contact>(data) },
            new ModuleDefinition { Id = "about", Label = "About", CreateViewModel = () => new ListPageViewModel<Contact>(data) });
        return new ShellViewModel(nav, new AvaloniaThemeManager()) { Title = "Demo" };
    }

    // Shell() builds an AvaloniaThemeManager, which needs a running Application — so these are
    // AvaloniaFacts. As plain Facts they only passed when some earlier test in the run happened to
    // leave Application.Current set, and failed whenever run in isolation.
    [AvaloniaFact]
    public void Palette_commands_come_from_modules_and_themes()
    {
        var shell = Shell();
        // 2 modules + 4 themes (the test app merges the all-in BirkoTheme.axaml)
        shell.PaletteCommands.Should().HaveCount(6);
        shell.PaletteCommands.Should().Contain(c => c.Label == "Go to Contacts");
        shell.PaletteCommands.Should().Contain(c => c.Label == "Theme: Neon");
    }

    [AvaloniaFact]
    public void Tenant_switcher_visibility_tracks_tenant_count()
    {
        var shell = Shell();
        shell.HasMultipleTenants.Should().BeFalse("no tenants configured");
        shell.Tenants = new[] { "A", "B" };
        shell.HasMultipleTenants.Should().BeTrue();
    }

    [AvaloniaFact]
    public void OpenPalette_command_opens_the_palette()
    {
        var shell = Shell();
        shell.IsPaletteOpen.Should().BeFalse();
        shell.OpenPaletteCommand.Execute(null);
        shell.IsPaletteOpen.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Palette_command_navigates()
    {
        var shell = Shell();
        shell.Nav.Navigate("contacts");
        var about = shell.PaletteCommands.First(c => c.Label == "Go to About");
        about.Run!.Invoke();
        shell.Nav.CurrentModule!.Id.Should().Be("about");
    }

    [AvaloniaFact]
    public void Ribbon_shell_hosts_the_ribbon_and_active_page()
    {
        var shell = Shell();
        shell.RibbonTabs = new[]
        {
            new Birko.Xaml.Core.Ribbon.RibbonTab
            {
                Id = "home", Label = "Home",
                Groups = new[]
                {
                    new Birko.Xaml.Core.Ribbon.RibbonGroup
                    {
                        Label = "Navigate",
                        Items = new[]
                        {
                            new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "c", Label = "Contacts", Icon = "\U0001F465", Run = () => shell.Nav.Navigate("contacts") },
                            new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "a", Label = "About", Icon = "ℹ", Run = () => shell.Nav.Navigate("about") },
                        },
                    },
                },
            },
        };
        shell.Nav.Navigate("contacts");

        var view = new RibbonShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        window.Show();
        window.Measure(new global::Avalonia.Size(800, 500));
        window.Arrange(new global::Avalonia.Rect(0, 0, 800, 500));

        view.GetVisualDescendants().OfType<Birko.Xaml.Avalonia.Controls.Ribbon>().Should().ContainSingle();
        view.GetVisualDescendants().OfType<ListPageView>().Should().ContainSingle("the active page renders in the content region");
    }

    [AvaloniaFact]
    public void Capture_ribbon_shell_screenshot()
    {
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;

        var shell = Shell();
        shell.RibbonTabs = new[]
        {
            new Birko.Xaml.Core.Ribbon.RibbonTab { Id = "home", Label = "Home", Groups = new[]
            {
                new Birko.Xaml.Core.Ribbon.RibbonGroup { Label = "Navigate", Items = new[]
                {
                    new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "c", Label = "Contacts", Icon = "\U0001F465", Run = () => shell.Nav.Navigate("contacts") },
                    new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "a", Label = "About", Icon = "ℹ" },
                }},
                new Birko.Xaml.Core.Ribbon.RibbonGroup { Label = "Records", Items = new[]
                {
                    new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "new", Label = "New", Icon = "➕" },
                    new Birko.Xaml.Core.Ribbon.RibbonItem { Id = "del", Label = "Delete", Icon = "\U0001F5D1" },
                }},
            }},
            new Birko.Xaml.Core.Ribbon.RibbonTab { Id = "view", Label = "View", Groups = Array.Empty<Birko.Xaml.Core.Ribbon.RibbonGroup>() },
        };
        shell.Nav.Navigate("contacts");

        var view = new RibbonShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 820, Height = 520 };
        window.Show();
        window.Measure(new global::Avalonia.Size(820, 520));
        window.Arrange(new global::Avalonia.Rect(0, 0, 820, 520));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "ribbon-shell.png"));
        view.GetVisualDescendants().OfType<Birko.Xaml.Avalonia.Controls.Ribbon>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Ctrl_K_binding_opens_the_palette_via_the_view()
    {
        var shell = Shell();
        var view = new ShellView { DataContext = shell };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        window.Show();
        window.Measure(new global::Avalonia.Size(800, 500));
        window.Arrange(new global::Avalonia.Rect(0, 0, 800, 500));

        // The view hosts a CommandPalette bound to the shell.
        view.GetVisualDescendants().OfType<Birko.Xaml.Avalonia.Controls.CommandPalette>().Should().ContainSingle();

        shell.OpenPaletteCommand.Execute(null);
        view.GetVisualDescendants().OfType<Birko.Xaml.Avalonia.Controls.CommandPalette>()
            .Single().IsOpen.Should().BeTrue("the palette IsOpen is bound to the shell");
    }
}
