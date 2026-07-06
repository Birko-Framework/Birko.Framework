using Birko.Xaml.Core.Mvvm;
using Birko.Xaml.Core.Navigation;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>Covers the mobile-shell nav-model on <see cref="ShellViewModel"/> — the bottom-nav
/// <see cref="MobileNavItem"/> list projected from modules, and its live active-surface tracking.</summary>
public class MobileShellViewModelTests
{
    private static ShellViewModel Build()
    {
        var nav = new NavigationService().Register(
            new ModuleDefinition { Id = "home", Label = "Home", Icon = "🏠", CreateViewModel = () => new object() },
            new ModuleDefinition { Id = "log", Label = "Log", Icon = "➕", CreateViewModel = () => new object() },
            new ModuleDefinition { Id = "stats", Label = "Stats", CreateViewModel = () => new object() });
        return new ShellViewModel(nav);
    }

    [Fact]
    public void NavItems_are_projected_from_modules_in_order()
    {
        var shell = Build();
        shell.NavItems.Select(i => i.Id).Should().Equal("home", "log", "stats");
        shell.NavItems[0].Label.Should().Be("Home");
        shell.NavItems[0].Icon.Should().Be("🏠");
        shell.NavItems[2].Icon.Should().BeNull("the third module has no icon");
    }

    [Fact]
    public void No_item_is_active_before_navigation()
    {
        Build().NavItems.Should().OnlyContain(i => i.IsActive == false);
    }

    [Fact]
    public void Navigation_activates_the_matching_item_only()
    {
        var shell = Build();
        shell.Nav.Navigate("log");

        shell.NavItems.Single(i => i.Id == "log").IsActive.Should().BeTrue();
        shell.NavItems.Where(i => i.Id != "log").Should().OnlyContain(i => i.IsActive == false);
    }

    [Fact]
    public void Switching_surface_moves_the_active_flag()
    {
        var shell = Build();
        shell.Nav.Navigate("home");
        shell.NavItems.Single(i => i.Id == "home").IsActive.Should().BeTrue();

        shell.Nav.Navigate("stats");
        shell.NavItems.Single(i => i.Id == "home").IsActive.Should().BeFalse();
        shell.NavItems.Single(i => i.Id == "stats").IsActive.Should().BeTrue();
    }

    [Fact]
    public void Back_restores_the_previous_active_surface()
    {
        var shell = Build();
        shell.Nav.Navigate("home");
        shell.Nav.Navigate("log");
        shell.Nav.Back();

        shell.NavItems.Single(i => i.Id == "home").IsActive.Should().BeTrue();
        shell.NavItems.Single(i => i.Id == "log").IsActive.Should().BeFalse();
    }

    [Fact]
    public void NavigateCommand_drives_the_active_surface()
    {
        var shell = Build();
        shell.NavigateCommand.Execute("stats");
        shell.NavItems.Single(i => i.Id == "stats").IsActive.Should().BeTrue();
    }
}
