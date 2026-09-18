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
        /// What a screen reader speaks in place of "button" — a ribbon <b>group</b>, which is what this
        /// control stands in for. The <i>state</i> is deliberately not in here: it belongs to
        /// <see cref="ExpandCollapseState"/> below, which Narrator does read, and which unlike this property
        /// raises a change notification when it flips.
        /// </summary>
        /// <remarks>
        /// This said "collapsed group" / "expanded group" for one build, on the theory that Narrator ignores
        /// expand/collapse on a plain <c>Button</c>. That was wrong, and instructively so: it was inferred
        /// from a report about the ribbon's <i>collapse chevron</i>, which announces only "button" and its
        /// name because it has no pattern at all. The chunk button's pattern was being spoken the whole time,
        /// so wording the state in here as well made Narrator say it twice ("Export, collapsed group,
        /// collapsed"). One fact, one owner.
        /// </remarks>
        protected override string GetLocalizedControlTypeCore() => "group";

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
