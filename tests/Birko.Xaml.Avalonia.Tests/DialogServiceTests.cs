using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Dialogs;
using Birko.Xaml.Core.Dialogs;
using Birko.Xaml.Core.Forms;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

public class DialogServiceTests
{
    // A window-spanning host Grid + a DialogService over it, shown and laid out.
    private static (Grid host, DialogService svc, Window window) Setup()
    {
        var host = new Grid();
        var window = new Window { Content = host, Width = 640, Height = 480 };
        window.Show();
        Layout(window);
        return (host, new DialogService(host), window);
    }

    private static void Layout(Window w)
    {
        w.Measure(new Size(640, 480));
        w.Arrange(new Rect(0, 0, 640, 480));
        Dispatcher.UIThread.RunJobs();
    }

    private static Modal LastModal(Panel host) => host.Children.OfType<Modal>().Last();

    private static Button FindButton(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == text);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task ConfirmAsync_confirm_resolves_true()
    {
        var (host, svc, window) = Setup();
        var task = svc.ConfirmAsync("Proceed?", new ConfirmOptions { ConfirmText = "Yes", CancelText = "No" });
        Layout(window);

        var modal = LastModal(host);
        modal.IsOpen.Should().BeTrue();
        Click(FindButton(modal, "Yes"));

        (await task).Should().BeTrue();
        host.Children.OfType<Modal>().Should().BeEmpty("the dialog is removed once answered");
    }

    [AvaloniaFact]
    public async Task ConfirmAsync_cancel_resolves_false()
    {
        var (host, svc, window) = Setup();
        var task = svc.ConfirmAsync("Proceed?", new ConfirmOptions { ConfirmText = "Yes", CancelText = "No" });
        Layout(window);
        Click(FindButton(LastModal(host), "No"));
        (await task).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ConfirmDeleteAsync_uses_danger_and_delete_defaults()
    {
        var (host, svc, window) = Setup();
        var task = svc.ConfirmDeleteAsync("Delete it?");
        Layout(window);

        var modal = LastModal(host);
        var confirm = FindButton(modal, "Delete"); // default confirm text
        confirm.Should().NotBeNull();
        Layout(window);
        // Danger variant → background bound to the danger token brush.
        Application.Current!.TryGetResource("BColorDangerBrush", Application.Current.ActualThemeVariant, out var danger);
        confirm.Background.Should().BeSameAs(danger);

        Click(FindButton(modal, "Cancel"));
        (await task).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AlertAsync_ok_completes_and_removes_modal()
    {
        var (host, svc, window) = Setup();
        var task = svc.AlertAsync("Saved.", title: "Notice", okText: "Got it");
        Layout(window);

        Click(FindButton(LastModal(host), "Got it"));
        await task; // completes
        host.Children.OfType<Modal>().Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task PromptAsync_returns_typed_value()
    {
        var (host, svc, window) = Setup();
        var task = svc.PromptAsync("Name?", new PromptOptions { ConfirmText = "OK" });
        Layout(window);

        var modal = LastModal(host);
        var input = modal.GetLogicalDescendants().OfType<TextBox>().First();
        input.Text = "Ada";
        Click(FindButton(modal, "OK"));

        (await task).Should().Be("Ada");
    }

    [AvaloniaFact]
    public async Task PromptAsync_required_blocks_empty_then_resolves()
    {
        var (host, svc, window) = Setup();
        var task = svc.PromptAsync("Name?", new PromptOptions { Required = true, ConfirmText = "OK" });
        Layout(window);

        var modal = LastModal(host);
        var ok = FindButton(modal, "OK");
        Click(ok); // empty → should NOT resolve
        task.IsCompleted.Should().BeFalse();

        var input = modal.GetLogicalDescendants().OfType<TextBox>().First();
        input.Text = "Grace";
        Click(ok);
        (await task).Should().Be("Grace");
    }

    [AvaloniaFact]
    public async Task ChooseAsync_returns_selected_value()
    {
        var (host, svc, window) = Setup();
        var task = svc.ChooseAsync("Format", new List<ChooseOption<string>>
        {
            new() { Label = "PDF", Value = "pdf" },
            new() { Label = "CSV", Value = "csv" },
            new() { Label = "Excel", Value = "xlsx" },
        });
        Layout(window);

        var modal = LastModal(host);
        modal.GetLogicalDescendants().OfType<Button>().Count(b => b.Content is "PDF" or "CSV" or "Excel").Should().Be(3);
        Click(FindButton(modal, "CSV"));

        (await task).Should().Be("csv");
    }

    [AvaloniaFact]
    public async Task PromptFormAsync_save_returns_model_cancel_returns_null()
    {
        var (host, svc, window) = Setup();
        var model = new PersonForm { First = "Zoe" };
        var fields = new[] { new FormField { Name = nameof(PersonForm.First), Label = "First" } };

        var saveTask = svc.PromptFormAsync(model, fields, "Person");
        Layout(window);
        Click(FindButton(LastModal(host), "Save"));
        (await saveTask).Should().BeSameAs(model);

        var cancelTask = svc.PromptFormAsync(model, fields);
        Layout(window);
        Click(FindButton(LastModal(host), "Cancel"));
        (await cancelTask).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task BusyAsync_shows_overlay_during_work_then_removes_it()
    {
        var (host, svc, window) = Setup();
        var gate = new TaskCompletionSource<int>();

        var busyTask = svc.BusyAsync(() => gate.Task, "Working…");
        Layout(window);

        bool HasBusyOverlay() => host.Children.OfType<Border>()
            .Any(b => b.GetLogicalDescendants().OfType<BusySpinner>().Any());
        HasBusyOverlay().Should().BeTrue("the spinner overlay is shown while work runs");

        gate.SetResult(42);
        (await busyTask).Should().Be(42);
        HasBusyOverlay().Should().BeFalse("the overlay is removed once work settles");
    }

    [AvaloniaFact]
    public void Notify_mounts_a_toast_in_a_container()
    {
        var (host, svc, _) = Setup();
        svc.Notify("Saved", NotifyVariant.Success);

        var container = host.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == "PART_ToastContainer");
        container.Should().NotBeNull();
        container!.Children.OfType<Border>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Capture_confirm_dialog_screenshot()
    {
        Application.Current!.RequestedThemeVariant = global::Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var host = new Grid();
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F8FAFC")) };
        host.Children.Add(page);
        var window = new Window { Content = host, Width = 560, Height = 380 };
        window.Show();

        var svc = new DialogService(host);
        _ = svc.ConfirmDeleteAsync("Delete this item? This action cannot be undone.");
        window.Measure(new Size(560, 380));
        window.Arrange(new Rect(0, 0, 560, 380));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "dialog-confirm-delete.png"));
        LastModal(host).IsOpen.Should().BeTrue();
    }

    private sealed class PersonForm
    {
        public string First { get; set; } = string.Empty;
    }
}
