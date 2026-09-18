namespace Birko.Xaml.Core.Device;

/// <summary>
/// Keeps the screen awake during a long uninterrupted activity (the XAML analogue of Birko.Web's
/// screen wake-lock). Platform-neutral (Avalonia-free); the concrete implementation lives in
/// <c>Birko.Xaml.Avalonia</c>. Both methods are idempotent and must never throw when the platform
/// doesn't support a wake lock — they degrade to a no-op.
///
/// <para>Re-acquire-on-resume: a platform wake lock is typically released by the OS when the app is
/// backgrounded, so an implementation should re-acquire on resume while <see cref="IsActive"/> is
/// true (the web equivalent is the <c>visibilitychange</c> re-acquire).</para>
/// </summary>
public interface IWakeLock
{
    /// <summary>True while a wake lock is held (or requested and pending re-acquire).</summary>
    bool IsActive { get; }

    /// <summary>Request a wake lock. Idempotent; safe to call when unsupported (no-op).</summary>
    Task AcquireAsync();

    /// <summary>Release the wake lock. Idempotent; safe to call when not held.</summary>
    Task ReleaseAsync();
}
