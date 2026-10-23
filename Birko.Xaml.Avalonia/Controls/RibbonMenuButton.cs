using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary><c>Ribbon.MenuItemInvoked</c>: a menu choice was picked, and the command it belongs to.</summary>
public sealed class RibbonMenuItemInvokedEventArgs : System.EventArgs
{
    public RibbonMenuItemInvokedEventArgs(string tabId, string itemId, string parentId)
    {
        TabId = tabId;
        ItemId = itemId;
        ParentId = parentId;
    }

    public string TabId { get; }

    /// <summary>The choice picked.</summary>
    public string ItemId { get; }

    /// <summary>The command whose menu it is.</summary>
    public string ParentId { get; }
}

/// <summary>
/// A button that opens a ribbon command's menu (TASK-755): a menu-only command, or a split command's ▾ part. A
/// <see cref="Button"/> whose peer reports expand/collapse state, so a screen reader hears that it opens a menu and
/// whether the menu is open — what b-ribbon says with <c>aria-haspopup="menu"</c> and <c>aria-expanded</c>.
/// </summary>
internal sealed class RibbonMenuButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : ButtonAutomationPeer, IExpandCollapseProvider
    {
        private readonly RibbonMenuButton _owner;

        public Peer(RibbonMenuButton owner) : base(owner)
        {
            _owner = owner;
            owner.PropertyChanged += (_, e) =>
            {
                if (e.Property != FlyoutProperty || owner.Flyout is not { } flyout) return;
                flyout.Opened += (_, _) => Raise();
                flyout.Closed += (_, _) => Raise();
            };
            if (owner.Flyout is { } f)
            {
                f.Opened += (_, _) => Raise();
                f.Closed += (_, _) => Raise();
            }
        }

        public ExpandCollapseState ExpandCollapseState =>
            _owner.Flyout?.IsOpen == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => true;

        public void Expand() => _owner.Flyout?.ShowAt(_owner);

        public void Collapse() => _owner.Flyout?.Hide();

        private void Raise() =>
            RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, null, ExpandCollapseState);
    }
}
