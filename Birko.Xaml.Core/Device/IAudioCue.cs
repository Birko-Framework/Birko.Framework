namespace Birko.Xaml.Core.Device;

/// <summary>Options for <see cref="IAudioCue.BeepAsync"/>.</summary>
public sealed class AudioCueOptions
{
    /// <summary>Tone frequency in Hz. Default 880.</summary>
    public int Frequency { get; set; } = 880;

    /// <summary>Tone length in milliseconds. Default 350.</summary>
    public int DurationMs { get; set; } = 350;

    /// <summary>Peak gain, 0–1 (honoured where the backend supports volume). Default 0.18.</summary>
    public double Gain { get; set; } = 0.18;

    /// <summary>Vibration pattern in milliseconds (mobile only). Omit to skip vibration.</summary>
    public int[]? Vibrate { get; set; }
}

/// <summary>
/// Plays a short audible cue (+ optional vibration) — e.g. a rest-timer chime. The XAML analogue of
/// Birko.Web's audio-cue, reduced to the portable "beep + vibrate" concept (the web version's
/// gesture-unlocked <c>AudioContext</c> / iOS audio-session priming is browser-specific and does not
/// transfer). Platform-neutral (Avalonia-free); the concrete impl lives in <c>Birko.Xaml.Avalonia</c>.
/// Best-effort: a no-op (never throws) where audio/vibration is unsupported.
/// </summary>
public interface IAudioCue
{
    /// <summary>Play a short tone (+ optional vibration). Best-effort; never throws.</summary>
    Task BeepAsync(AudioCueOptions? options = null);
}
