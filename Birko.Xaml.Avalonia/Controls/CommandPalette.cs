using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Birko.Xaml.Core.Commands;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Fuzzy command palette (the XAML port of <c>b-command-palette</c>): an overlay with a search box
/// that filters <see cref="Commands"/> as you type, keyboard-navigable (Up/Down/Enter/Esc), invoking
/// the chosen <see cref="CommandItem.Run"/>. Toggle with <see cref="IsOpen"/> (e.g. wire Ctrl+K).
/// </summary>
[PseudoClasses(":open")]
[TemplatePart("PART_Backdrop", typeof(Control))]
[TemplatePart("PART_SearchBox", typeof(TextBox))]
[TemplatePart("PART_List", typeof(ListBox))]
public class CommandPalette : TemplatedControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<CommandPalette, bool>(nameof(IsOpen));

    public static readonly StyledProperty<IEnumerable<CommandItem>?> CommandsProperty =
        AvaloniaProperty.Register<CommandPalette, IEnumerable<CommandItem>?>(nameof(Commands));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<CommandPalette, string?>(nameof(SearchText));

    public static readonly DirectProperty<CommandPalette, IReadOnlyList<CommandItem>> FilteredCommandsProperty =
        AvaloniaProperty.RegisterDirect<CommandPalette, IReadOnlyList<CommandItem>>(
            nameof(FilteredCommands), o => o.FilteredCommands);

    private IReadOnlyList<CommandItem> _filtered = System.Array.Empty<CommandItem>();
    private TextBox? _search;
    private ListBox? _list;

    static CommandPalette()
    {
        IsOpenProperty.Changed.AddClassHandler<CommandPalette>((p, _) => p.OnIsOpenChanged());
        CommandsProperty.Changed.AddClassHandler<CommandPalette>((p, _) => p.Refilter());
        SearchTextProperty.Changed.AddClassHandler<CommandPalette>((p, _) => p.Refilter());
    }

    public CommandPalette() => UpdatePseudoClasses();

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public IEnumerable<CommandItem>? Commands { get => GetValue(CommandsProperty); set => SetValue(CommandsProperty, value); }
    public string? SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }

    public IReadOnlyList<CommandItem> FilteredCommands
    {
        get => _filtered;
        private set => SetAndRaise(FilteredCommandsProperty, ref _filtered, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Control>("PART_Backdrop") is { } backdrop)
            backdrop.PointerPressed += (_, _) => Close();

        _search = e.NameScope.Find<TextBox>("PART_SearchBox");
        _list = e.NameScope.Find<ListBox>("PART_List");
        if (_search is not null)
            _search.KeyDown += OnSearchKeyDown;
        if (_list is not null)
            _list.DoubleTapped += (_, _) => InvokeSelected();

        Refilter();
        if (IsOpen) FocusSearch();
    }

    private void OnIsOpenChanged()
    {
        UpdatePseudoClasses();
        if (IsOpen)
        {
            SearchText = string.Empty;
            Refilter();
            FocusSearch();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_list is null) return;
        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = System.Math.Min(_list.SelectedIndex + 1, FilteredCommands.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = System.Math.Max(_list.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Refilter()
    {
        var all = Commands ?? Enumerable.Empty<CommandItem>();
        var q = SearchText;
        FilteredCommands = string.IsNullOrWhiteSpace(q)
            ? all.ToList()
            : all.Where(c => c.Label.Contains(q!, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (_list is not null && FilteredCommands.Count > 0)
            _list.SelectedIndex = 0;
    }

    /// <summary>Invoke the selected command and close. Public so a host can bind Enter/click elsewhere.</summary>
    public void InvokeSelected()
    {
        int i = _list?.SelectedIndex ?? -1;
        if (i >= 0 && i < FilteredCommands.Count)
        {
            var item = FilteredCommands[i];
            Close();
            item.Run?.Invoke();
        }
    }

    private void Close()
    {
        IsOpen = false;
        SearchText = string.Empty;
    }

    private void FocusSearch() => Dispatcher.UIThread.Post(() => _search?.Focus());

    private void UpdatePseudoClasses() => PseudoClasses.Set(":open", IsOpen);
}
