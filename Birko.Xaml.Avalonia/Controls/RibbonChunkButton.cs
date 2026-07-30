using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// The button a collapsed ribbon group folds into. A <see cref="Button"/> whose only addition is an
/// automation peer that reports <b>expand/collapse state</b>, so a screen reader announces "collapsed,
/// expandable" rather than just "button".
/// </summary>
/// <remarks>
/// This matters more here than it looks. Office can afford to hide commands behind a collapsed group partly
/// because KeyTips reach everything by keystroke regardless of layout; Birko has no KeyTips yet, so a
/// collapsed group is the only route to those commands. If it announces as a bare button, narrowing the
/// window removes commands from screen-reader users specifically — the same defect STORY-049 exists to
/// remove for sighted mouse users. `b-ribbon` gets this from `aria-expanded` + `aria-haspopup`; XAML needs
/// a peer, because there is no attribute to set.
/// </remarks>
internal sealed class RibbonChunkButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RibbonChunkButtonAutomationPeer(this);

    /// <summary>Whether this button's flyout is currently open.</summary>
    internal bool IsFlyoutOpen => Flyout?.IsOpen == true;

    private sealed class RibbonChunkButtonAutomationPeer : ButtonAutomationPeer, IExpandCollapseProvider
    {
        private readonly RibbonChunkButton _owner;

        public RibbonChunkButtonAutomationPeer(RibbonChunkButton owner) : base(owner)
        {
            _owner = owner;

            // Raise a property change when the flyout opens or closes, or assistive tech keeps reading the
            // state it saw first. A peer that reports a stale state is arguably worse than none.
            if (owner.Flyout is { } flyout)
            {
                flyout.Opened += (_, _) => RaiseExpandCollapseChanged();
                flyout.Closed += (_, _) => RaiseExpandCollapseChanged();
            }
        }

        /// <summary>
        /// What Narrator actually speaks in place of "button". The reviewer heard exactly that — "button"
        /// and the name — because the <see cref="IExpandCollapseProvider"/> pattern alone was not enough:
        /// Narrator voices expand/collapse state for the control types where it expects it (combo box, tree
        /// item, menu item), and a plain <b>Button</b> is not one of them, whatever patterns it advertises.
        /// UIA's <c>LocalizedControlType</c> is the supported way to say what a control really is, so the
        /// state travels in the thing Narrator is guaranteed to read.
        /// </summary>
        /// <remarks>
        /// Deliberately dynamic rather than the fixed string "collapsed group". A chunk button only exists
        /// while its group is collapsed, so the static wording would be truthful about the control — but it
        /// would then keep saying "collapsed" with the flyout open. Clients re-read this on focus, and focus
        /// returns to the chunk when its flyout closes, so the announcement matches what is on screen.
        /// </remarks>
        protected override string GetLocalizedControlTypeCore() =>
            _owner.IsFlyoutOpen ? "expanded group" : "collapsed group";

        public ExpandCollapseState ExpandCollapseState =>
            _owner.IsFlyoutOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => true;

        public void Expand() => _owner.Flyout?.ShowAt(_owner);

        public void Collapse() => _owner.Flyout?.Hide();

        private void RaiseExpandCollapseChanged() =>
            RaisePropertyChangedEvent(ExpandCollapseProviderProperty, null, ExpandCollapseState);

        private static readonly AutomationProperty ExpandCollapseProviderProperty =
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty;
    }
}
