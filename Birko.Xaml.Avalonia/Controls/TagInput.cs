using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>Why <see cref="TagInput"/> refused a tag — the web's <c>reject</c> reasons, and only those.</summary>
public enum TagRejectReason { MaxCount, Duplicate }

/// <summary>A tag <see cref="TagInput"/> refused, and why.</summary>
public sealed class TagRejectedEventArgs(string tag, TagRejectReason reason) : EventArgs
{
    public string Tag { get; } = tag;

    public TagRejectReason Reason { get; } = reason;
}

/// <summary>A tag added or removed, with the tags after the change.</summary>
public sealed class TagEventArgs(string tag, IReadOnlyList<string> tags) : EventArgs
{
    public string Tag { get; } = tag;

    public IReadOnlyList<string> Tags { get; } = tags;
}

/// <summary>
/// A list of short values typed as removable chips — the XAML port of <c>b-tag-input</c> (FEATURE-020 / TASK-774), and
/// what the <c>FieldType.Tags</c> form field renders. Typing a separator (default comma, newline, tab) commits the text
/// before it; Enter, Tab with text, and leaving the field commit the rest; Backspace in the empty box removes the last
/// tag; pasting text that holds a separator adds every part. A duplicate (unless <see cref="AllowDuplicates"/>) or a tag
/// past <see cref="MaxCount"/> is refused with <see cref="TagRejected"/>, as on the web.
/// </summary>
public class TagInput : ContentControl
{
    private const string DefaultSeparators = ",\n\t";

    public static readonly StyledProperty<IReadOnlyList<string>> TagsProperty =
        AvaloniaProperty.Register<TagInput, IReadOnlyList<string>>(nameof(Tags), Array.Empty<string>(), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<TagInput, string?>(nameof(Placeholder));
    public static readonly StyledProperty<string?> SeparatorsProperty = AvaloniaProperty.Register<TagInput, string?>(nameof(Separators));
    public static readonly StyledProperty<int> MaxCountProperty = AvaloniaProperty.Register<TagInput, int>(nameof(MaxCount));
    public static readonly StyledProperty<bool> AllowDuplicatesProperty = AvaloniaProperty.Register<TagInput, bool>(nameof(AllowDuplicates));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<TagInput, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> HasErrorProperty = AvaloniaProperty.Register<TagInput, bool>(nameof(HasError));

    static TagInput()
    {
        TagsProperty.Changed.AddClassHandler<TagInput>((t, _) => t.OnTagsSet());
        foreach (var p in new AvaloniaProperty[] { PlaceholderProperty, IsReadOnlyProperty, HasErrorProperty, IsEnabledProperty })
            p.Changed.AddClassHandler<TagInput>((t, _) => t.Render());
    }

    private readonly List<string> _tags = new();
    private readonly TextBox _input;
    private readonly WrapPanel _wrap = new() { Orientation = Orientation.Horizontal };
    private readonly Border _container;
    private bool _syncing;

    public TagInput()
    {
        _input = new TextBox
        {
            MinWidth = 96,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 2),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "tag-buffer" },
        };
        // Tunnel, so the key is seen before the TextBox acts on it (Backspace in an empty box, Enter, Tab).
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        _input.TextChanged += (_, _) => ProcessBuffer();
        _input.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(_input.Text)) CommitBuffer(); };
        _input.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting, RoutingStrategies.Bubble);

        _container = new Border { Child = _wrap, Padding = new Thickness(6, 3), Classes = { "tag-container" } };
        Bind(_container, Border.BackgroundProperty, "BBgBrush");
        Bind(_container, Border.BorderBrushProperty, "BBorderBrush");
        _container.BorderThickness = new Thickness(1);
        _container.Bind(Border.CornerRadiusProperty, _container.GetResourceObservable("BRadius"));
        _container.Bind(MinHeightProperty, _container.GetResourceObservable("BControlMinHeight"));
        // A click on the field's empty space puts the caret in the box, as the web's container does.
        _container.PointerPressed += (_, e) => { if (e.Source is not Button) _input.Focus(); };
        _input.GotFocus += (_, _) => Render();
        _input.LostFocus += (_, _) => Render();
        Content = _container;
        Render();
    }

    /// <summary>The tags, in order. Two-way bindable; a set list is taken through the same rules (duplicates, max count) without events.</summary>
    public IReadOnlyList<string> Tags { get => GetValue(TagsProperty); set => SetValue(TagsProperty, value); }

    /// <summary>Shown while there are no tags.</summary>
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>The characters that end a tag. <c>null</c> or empty: comma, newline and tab.</summary>
    public string? Separators { get => GetValue(SeparatorsProperty); set => SetValue(SeparatorsProperty, value); }

    /// <summary>The most tags allowed; 0 for no limit.</summary>
    public int MaxCount { get => GetValue(MaxCountProperty); set => SetValue(MaxCountProperty, value); }

    /// <summary>Lets the same tag appear twice.</summary>
    public bool AllowDuplicates { get => GetValue(AllowDuplicatesProperty); set => SetValue(AllowDuplicatesProperty, value); }

    /// <summary>Shows the tags without the box or the remove buttons.</summary>
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    /// <summary>Draws the field's border in the danger colour (web <c>error</c>).</summary>
    public bool HasError { get => GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }

    /// <summary>The tags joined with commas (web <c>value</c>). Setting it splits on commas.</summary>
    public string Value
    {
        get => string.Join(",", _tags);
        set => SetTags((value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
    }

    /// <summary>The text typed but not yet a tag.</summary>
    public string PendingText => _input.Text ?? "";

    /// <summary>A tag was added by the user.</summary>
    public event EventHandler<TagEventArgs>? TagAdded;

    /// <summary>A tag was removed by the user.</summary>
    public event EventHandler<TagEventArgs>? TagRemoved;

    /// <summary>A tag was refused: a duplicate, or past <see cref="MaxCount"/>.</summary>
    public event EventHandler<TagRejectedEventArgs>? TagRejected;

    /// <summary>The tags changed through the user (web <c>change</c>). Not raised by <see cref="SetTags"/> or setting <see cref="Tags"/>.</summary>
    public event EventHandler? TagsChanged;

    /// <summary>Replaces the tags, through the same rules, without raising events (web <c>setTags</c>).</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        foreach (var t in tags) AddTag(t, raise: false);
        _input.Text = "";
        Publish();
        Render();
    }

    /// <summary>Removes every tag and the pending text, and raises <see cref="TagsChanged"/> (web <c>clear</c>).</summary>
    public void Clear()
    {
        _tags.Clear();
        _input.Text = "";
        Publish();
        TagsChanged?.Invoke(this, EventArgs.Empty);
        Render();
    }

    private void OnTagsSet()
    {
        if (_syncing) return;
        var incoming = Tags ?? Array.Empty<string>();
        if (incoming.SequenceEqual(_tags)) return;
        _tags.Clear();
        foreach (var t in incoming) AddTag(t, raise: false);
        // The rules may have dropped some: publish what is actually held.
        if (!incoming.SequenceEqual(_tags)) Publish();
        Render();
    }

    private void Publish()
    {
        _syncing = true;
        try { SetCurrentValue(TagsProperty, _tags.ToArray()); }
        finally { _syncing = false; }
    }

    private Regex SplitPattern()
    {
        var raw = string.IsNullOrEmpty(Separators) ? DefaultSeparators : Separators;
        return new Regex("[" + Regex.Escape(raw).Replace("]", "\\]").Replace("-", "\\-") + "]", RegexOptions.CultureInvariant);
    }

    private bool AddTag(string tag, bool raise)
    {
        if (tag.Length == 0) return false;
        if (MaxCount > 0 && _tags.Count >= MaxCount)
        {
            if (raise) TagRejected?.Invoke(this, new TagRejectedEventArgs(tag, TagRejectReason.MaxCount));
            return false;
        }
        if (!AllowDuplicates && _tags.Contains(tag))
        {
            if (raise) TagRejected?.Invoke(this, new TagRejectedEventArgs(tag, TagRejectReason.Duplicate));
            return false;
        }
        _tags.Add(tag);
        if (raise) TagAdded?.Invoke(this, new TagEventArgs(tag, _tags.ToArray()));
        return true;
    }

    private void Changed()
    {
        Publish();
        TagsChanged?.Invoke(this, EventArgs.Empty);
        Render();
    }

    /// <summary>A separator typed: commit the tags before it, keep what follows it in the box.</summary>
    private bool _processing;

    private void ProcessBuffer()
    {
        if (_processing) return;
        var text = _input.Text ?? "";
        var pattern = SplitPattern();
        if (text.Length == 0 || !pattern.IsMatch(text)) return;
        var parts = pattern.Split(text);
        var tail = parts[^1];
        var changed = false;
        foreach (var p in parts.Take(parts.Length - 1))
        {
            var tag = p.Trim();
            if (tag.Length > 0 && AddTag(tag, raise: true)) changed = true;
        }
        _processing = true;
        try
        {
            _input.Text = tail;
            _input.CaretIndex = tail.Length;
        }
        finally { _processing = false; }
        if (changed) Changed();
    }

    private void CommitBuffer()
    {
        var tag = (_input.Text ?? "").Trim();
        _processing = true;
        try { _input.Text = ""; }
        finally { _processing = false; }
        if (tag.Length > 0 && AddTag(tag, raise: true)) Changed();
    }

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= _tags.Count) return;
        var removed = _tags[index];
        _tags.RemoveAt(index);
        TagRemoved?.Invoke(this, new TagEventArgs(removed, _tags.ToArray()));
        Changed();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsReadOnly) return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                CommitBuffer();
                break;
            case Key.Back when string.IsNullOrEmpty(_input.Text) && _tags.Count > 0:
                e.Handled = true;
                RemoveAt(_tags.Count - 1);
                break;
            case Key.Tab when !string.IsNullOrWhiteSpace(_input.Text):
                e.Handled = true;
                CommitBuffer();
                break;
        }
    }

    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        // Decided before the await: the TextBox pastes on its own unless the event is handled now.
        e.Handled = true;
        var text = await clipboard.TryGetTextAsync() ?? "";
        PasteText(text);
    }

    /// <summary>
    /// What pasting <paramref name="text"/> does: with a separator in it, every part becomes a tag (web <c>paste</c>);
    /// without one, it is inserted into the box at the caret like any paste.
    /// </summary>
    public void PasteText(string text)
    {
        if (text.Length == 0 || IsReadOnly) return;
        var pattern = SplitPattern();
        if (!pattern.IsMatch(text))
        {
            var caret = Math.Clamp(_input.CaretIndex, 0, (_input.Text ?? "").Length);
            _input.Text = (_input.Text ?? "").Insert(caret, text);
            _input.CaretIndex = caret + text.Length;
            return;
        }
        var changed = false;
        foreach (var part in pattern.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0))
            if (AddTag(part, raise: true)) changed = true;
        if (changed) Changed();
    }

    // ── Rendering ──

    private void Render()
    {
        // The text box stays in the panel and only the chips before it are replaced: detaching the box would take focus
        // from it, and its LostFocus handler re-enters here (found by TagInputTests: the box was added twice).
        if (!_wrap.Children.Contains(_input)) _wrap.Children.Add(_input);
        while (_wrap.Children.Count > 1) _wrap.Children.RemoveAt(0);
        for (var i = 0; i < _tags.Count; i++) _wrap.Children.Insert(i, Chip(_tags[i], i));
        _input.Watermark = _tags.Count == 0 ? Placeholder : null;
        _input.IsVisible = !IsReadOnly;
        _input.IsEnabled = IsEnabled;

        var focused = _input.IsFocused;
        Bind(_container, Border.BorderBrushProperty, HasError ? "BColorDangerBrush" : focused ? "BBorderFocusBrush" : "BBorderBrush");
        _container.Opacity = IsEnabled ? 1 : 0.5;
        _container.Classes.Set("has-error", HasError);
    }

    private Control Chip(string tag, int index)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var text = new TextBlock { Text = tag, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 };
        Bind(text, TextBlock.ForegroundProperty, "BTextBrush");
        row.Children.Add(text);
        if (!IsReadOnly && IsEnabled)
        {
            var remove = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 0),
                MinHeight = 0,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "chip-remove" },
            };
            Bind(remove, ForegroundProperty, "BTextMutedBrush");
            // A glyph is not a name: say which tag the button removes (see this project's CLAUDE.md § Accessibility).
            AutomationProperties.SetName(remove, $"Remove {tag}");
            ToolTip.SetTip(remove, $"Remove {tag}");
            remove.Click += (_, e) => { e.Handled = true; RemoveAt(index); };
            row.Children.Add(remove);
        }
        var chip = new Border
        {
            Child = row,
            Padding = new Thickness(8, 1),
            Margin = new Thickness(0, 2, 4, 2),
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "chip" },
        };
        Bind(chip, Border.BackgroundProperty, "BBgTertiaryBrush");
        return chip;
    }

    private static void Bind(AvaloniaObject target, AvaloniaProperty property, string resourceKey)
    {
        if (target is StyledElement s) s.Bind(property, s.GetResourceObservable(resourceKey));
    }
}
