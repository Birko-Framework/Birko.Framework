using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Avalonia.Controls
{
    /// <summary>
    /// A small offline / syncing / synced chip — the XAML analogue of Birko.Web's
    /// <c>&lt;b-sync-status&gt;</c>. Bind <see cref="Status"/> to a <see cref="MirrorDataSource{T}.Status"/>
    /// (or any <see cref="SyncStatus"/> source). Text is localized via <see cref="I18n.Instance"/>
    /// (keys <c>bxaml.sync.{synced,syncing,offline}</c>, English fallback) and the foreground follows
    /// the status token brush (success / warning / danger). Reuses the badge look; no template needed.
    /// </summary>
    public class SyncStatusIndicator : ContentControl
    {
        public static readonly StyledProperty<SyncStatus> StatusProperty =
            AvaloniaProperty.Register<SyncStatusIndicator, SyncStatus>(nameof(Status));

        private IDisposable? _foregroundSubscription;

        public SyncStatusIndicator()
        {
            Classes.Add("sync-status");
            UpdateVisual();
        }

        /// <summary>The offline/sync state to display.</summary>
        public SyncStatus Status
        {
            get => GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == StatusProperty)
            {
                UpdateVisual();
            }
        }

        private void UpdateVisual()
        {
            var (key, fallback, brushKey, styleClass) = Status switch
            {
                SyncStatus.Synced => ("bxaml.sync.synced", "Synced", "BColorSuccessBrush", "synced"),
                SyncStatus.Syncing => ("bxaml.sync.syncing", "Syncing…", "BColorWarningBrush", "syncing"),
                _ => ("bxaml.sync.offline", "Offline", "BColorDangerBrush", "offline"),
            };

            var text = I18n.Instance[key];
            Content = text == key ? fallback : text; // English fallback when no locale is registered

            // Status class for consumer styling (mirrors the web chip's state class).
            Classes.Remove("synced");
            Classes.Remove("syncing");
            Classes.Remove("offline");
            Classes.Add(styleClass);

            // Foreground follows the token brush, resolved live (null-safe if the token is absent).
            _foregroundSubscription?.Dispose();
            _foregroundSubscription = this.Bind(ForegroundProperty, this.GetResourceObservable(brushKey));
        }
    }
}
