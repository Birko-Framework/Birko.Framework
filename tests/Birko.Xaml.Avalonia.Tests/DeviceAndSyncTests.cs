using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Device;
using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Device;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

public class WakeLockTests
{
    [Fact]
    public async Task Acquire_and_release_toggle_IsActive()
    {
        var wl = new AvaloniaWakeLock();
        wl.IsActive.Should().BeFalse();

        await wl.AcquireAsync();
        wl.IsActive.Should().BeTrue();

        await wl.AcquireAsync(); // idempotent
        wl.IsActive.Should().BeTrue();

        await wl.ReleaseAsync();
        wl.IsActive.Should().BeFalse();

        await wl.ReleaseAsync(); // idempotent, no throw
        wl.IsActive.Should().BeFalse();
    }
}

public class AudioCueTests
{
    [Fact]
    public async Task Beep_is_best_effort_and_never_throws()
    {
        var cue = new AvaloniaAudioCue();
        // Short/near-silent so a Windows CI agent doesn't actually blare; must not throw anywhere.
        await cue.Awaiting(c => c.BeepAsync(new AudioCueOptions { Frequency = 440, DurationMs = 1 }))
            .Should().NotThrowAsync();
        await cue.Awaiting(c => c.BeepAsync()).Should().NotThrowAsync();
    }
}

public class SyncStatusIndicatorTests
{
    [AvaloniaFact]
    public void Reflects_each_sync_state_in_content_and_class()
    {
        var chip = new SyncStatusIndicator();
        // Default (Synced)
        chip.Content.Should().Be("Synced");
        chip.Classes.Should().Contain("synced");

        chip.Status = SyncStatus.Syncing;
        chip.Content.Should().Be("Syncing…");
        chip.Classes.Should().Contain("syncing");
        chip.Classes.Should().NotContain("synced");

        chip.Status = SyncStatus.Offline;
        chip.Content.Should().Be("Offline");
        chip.Classes.Should().Contain("offline");
    }

    [AvaloniaFact]
    public void Renders_hosted_in_a_window()
    {
        var chip = new SyncStatusIndicator { Status = SyncStatus.Offline };
        var window = new Window { Content = chip, Width = 200, Height = 80 };
        window.Show();
        window.Measure(new global::Avalonia.Size(200, 80));
        window.Arrange(new global::Avalonia.Rect(0, 0, 200, 80));
        chip.Content.Should().Be("Offline");
    }
}
