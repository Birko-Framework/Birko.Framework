using System.Threading.Tasks;
using Birko.Xaml.Core.Device;

namespace Birko.Xaml.Avalonia.Device
{
    /// <summary>
    /// <see cref="IWakeLock"/> for Avalonia. Desktop (net8.0) has no OS wake-lock API, so this tracks
    /// the requested state and otherwise no-ops. It is structured so a mobile backend (Android
    /// <c>FLAG_KEEP_SCREEN_ON</c> / iOS <c>isIdleTimerDisabled</c>) can slot in behind
    /// <see cref="AcquireCore"/> / <see cref="ReleaseCore"/> once <c>Birko.Xaml.Avalonia</c> targets
    /// mobile TFMs (tracked in EPIC-016 / STORY-040).
    /// </summary>
    public class AvaloniaWakeLock : IWakeLock
    {
        /// <inheritdoc />
        public bool IsActive { get; private set; }

        /// <inheritdoc />
        public Task AcquireAsync()
        {
            if (!IsActive)
            {
                IsActive = true;
                AcquireCore();
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ReleaseAsync()
        {
            if (IsActive)
            {
                IsActive = false;
                ReleaseCore();
            }
            return Task.CompletedTask;
        }

        /// <summary>Platform hook — desktop no-op. Override / #if a mobile TFM to hold a real lock.</summary>
        protected virtual void AcquireCore() { }

        /// <summary>Platform hook — desktop no-op.</summary>
        protected virtual void ReleaseCore() { }
    }
}
