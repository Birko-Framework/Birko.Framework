using System;
using System.Threading.Tasks;
using Birko.Xaml.Core.Device;

namespace Birko.Xaml.Avalonia.Device
{
    /// <summary>
    /// <see cref="IAudioCue"/> for Avalonia. Best-effort desktop tone via <c>Console.Beep</c> on
    /// Windows (off the UI thread); a no-op elsewhere (Avalonia has no portable audio API) and for
    /// vibration (mobile-only). Never throws. A mobile backend can override <see cref="BeepCore"/> to
    /// use the platform tone/haptics once <c>Birko.Xaml.Avalonia</c> targets mobile TFMs.
    /// </summary>
    public class AvaloniaAudioCue : IAudioCue
    {
        /// <inheritdoc />
        public Task BeepAsync(AudioCueOptions? options = null)
        {
            var o = options ?? new AudioCueOptions();
            // Off the UI thread — Console.Beep blocks for the tone duration.
            return Task.Run(() =>
            {
                try { BeepCore(o); }
                catch { /* best-effort: a cue must never take the app down */ }
            });
        }

        /// <summary>Platform hook. Windows desktop plays a tone; other desktops no-op.</summary>
        protected virtual void BeepCore(AudioCueOptions o)
        {
            if (OperatingSystem.IsWindows())
            {
                // Console.Beep accepts 37..32767 Hz.
                var freq = Math.Clamp(o.Frequency, 37, 32767);
                Console.Beep(freq, Math.Max(1, o.DurationMs));
            }
            // Non-Windows desktop: no portable tone; vibration is mobile-only → no-op.
        }
    }
}
