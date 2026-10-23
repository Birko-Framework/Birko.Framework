using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary><c>Ribbon.ItemToggled</c>: a toggle or one-of member was pressed, and its new state.</summary>
public sealed class RibbonItemToggledEventArgs : System.EventArgs
{
    public RibbonItemToggledEventArgs(string tabId, string itemId, bool pressed)
    {
        TabId = tabId;
        ItemId = itemId;
        Pressed = pressed;
    }

    public string TabId { get; }
    public string ItemId { get; }
    public bool Pressed { get; }
}

/// <summary>
/// A ribbon command with a pressed state (TASK-754, as b-ribbon's TASK-739): an on/off toggle (Bold), or one member of
/// a one-of set (alignment). A <see cref="Button"/>, not a <c>ToggleButton</c>: the ribbon owns the state (a one-of
/// member that is on stays on when pressed again), and the toolkit's checked look would fight the tinted, outlined one
/// the user picked. Its automation peer reports the state: a toggle, or a radio button that is selected or not.
/// </summary>
internal sealed class RibbonToggleCommand : Button
{
    // Styled as a Button: without this a subclass gets no Birko theme (Controls/Buttons.axaml is keyed by Button) and
    // falls back to the base one, whose font set its labels apart from the plain commands' (seen by the user, 2026-10-23).
    protected override System.Type StyleKeyOverride => typeof(Button);

    private bool _pressed;
    private RibbonToggleCommandPeerBase? _peer;

    /// <summary>A member of a one-of set (announced as a radio button), rather than an on/off toggle.</summary>
    public bool IsRadio { get; init; }

    /// <summary>Whether the command is on. Set by the ribbon, which owns the state.</summary>
    public bool IsPressed
    {
        get => _pressed;
        set
        {
            if (_pressed == value) return;
            _pressed = value;
            _peer?.RaiseStateChanged(!value, value);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _peer = IsRadio ? new RadioPeer(this) : new TogglePeer(this);

    private abstract class RibbonToggleCommandPeerBase : ButtonAutomationPeer
    {
        protected RibbonToggleCommandPeerBase(RibbonToggleCommand owner) : base(owner) => Owner = owner;
        protected new RibbonToggleCommand Owner { get; }
        public abstract void RaiseStateChanged(bool oldValue, bool newValue);
        protected void Press() => ((IInvokeProvider)this).Invoke();
    }

    private sealed class TogglePeer : RibbonToggleCommandPeerBase, IToggleProvider
    {
        public TogglePeer(RibbonToggleCommand owner) : base(owner) { }

        public ToggleState ToggleState => Owner.IsPressed ? ToggleState.On : ToggleState.Off;

        public void Toggle() => Press();

        public override void RaiseStateChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                oldValue ? ToggleState.On : ToggleState.Off, newValue ? ToggleState.On : ToggleState.Off);
    }

    private sealed class RadioPeer : RibbonToggleCommandPeerBase, ISelectionItemProvider
    {
        public RadioPeer(RibbonToggleCommand owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;

        public bool IsSelected => Owner.IsPressed;

        public ISelectionProvider? SelectionContainer => null;

        public void Select() { if (!Owner.IsPressed) Press(); }

        public void AddToSelection() => Select();

        public void RemoveFromSelection() { } // a one-of set always has one member on

        public override void RaiseStateChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue);
    }
}
