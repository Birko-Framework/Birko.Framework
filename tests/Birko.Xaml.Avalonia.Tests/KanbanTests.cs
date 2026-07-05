using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Kanban;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035: the kanban board (b-kanban) — columns, cards, reactive moves.</summary>
public class KanbanTests
{
    private static (Kanban board, KanbanColumn todo, KanbanColumn done) Build()
    {
        var todo = new KanbanColumn { Id = "todo", Title = "To Do" };
        todo.Cards.Add(new KanbanCard { Id = "1", Title = "Task A" });
        todo.Cards.Add(new KanbanCard { Id = "2", Title = "Task B" });
        var done = new KanbanColumn { Id = "done", Title = "Done" };
        done.Cards.Add(new KanbanCard { Id = "3", Title = "Task C" });
        return (new Kanban { Columns = new[] { todo, done } }, todo, done);
    }

    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 640, Height = 360 };
        window.Show();
        window.Measure(new Size(640, 360));
        window.Arrange(new Rect(0, 0, 640, 360));
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    [AvaloniaFact]
    public void Renders_columns_and_cards()
    {
        var (board, _, _) = Build();
        Show(board);
        var texts = board.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain("To Do");
        texts.Should().Contain("Done");
        texts.Should().Contain("Task A");
        texts.Should().Contain("Task C");
    }

    [AvaloniaFact]
    public void Moving_a_card_updates_the_bound_columns()
    {
        var (board, todo, done) = Build();
        Show(board);
        var lists = board.GetVisualDescendants().OfType<ItemsControl>()
            .Where(ic => ic.ItemsSource is System.Collections.ObjectModel.ObservableCollection<KanbanCard>)
            .ToList();
        lists.Should().HaveCount(2);
        todo.Cards.Should().HaveCount(2);
        done.Cards.Should().HaveCount(1);

        var card = todo.Cards[0];
        todo.Cards.Remove(card);
        done.Cards.Add(card);

        todo.Cards.Should().HaveCount(1);
        done.Cards.Should().HaveCount(2);
        // the bound ItemsControls reflect the observable collections
        lists.First(l => ReferenceEquals(l.ItemsSource, done.Cards)).ItemCount.Should().Be(2);
    }

    [AvaloniaFact]
    public void Capture_kanban_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        KanbanColumn Col(string id, string title, params (string, string)[] cards)
        {
            var c = new KanbanColumn { Id = id, Title = title };
            foreach (var (t, d) in cards) c.Cards.Add(new KanbanCard { Id = t, Title = t, Description = d });
            return c;
        }

        var board = new Kanban
        {
            Margin = new Thickness(16),
            Columns = new[]
            {
                Col("todo", "To Do", ("Design tokens", "Single source"), ("Theme system", "Runtime swap")),
                Col("doing", "In Progress", ("Tier-1 controls", "Restyle natives")),
                Col("done", "Done", ("Gallery", "Go/no-go gate"), ("Core VMs", "MVVM bases")),
            },
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F1F5F9")), Child = board };
        var window = new Window { Content = page, Width = 720, Height = 340 };
        window.Show();
        window.Measure(new Size(720, 340));
        window.Arrange(new Rect(0, 0, 720, 340));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "kanban.png"));
        board.GetVisualDescendants().OfType<TextBlock>().Should().NotBeEmpty();
    }
}
