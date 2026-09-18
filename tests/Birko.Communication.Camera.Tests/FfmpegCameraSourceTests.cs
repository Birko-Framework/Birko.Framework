using Birko.Communication.Camera.Cameras;
using FluentAssertions;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Communication.Camera.Tests;

public class FfmpegCameraSourceTests
{
    [Fact]
    public async Task CaptureFrameAsync_WithVerboseStderr_CompletesWithoutDeadlock()
    {
        // Regression for CR-H019: the capture drained only stdout while stderr was also
        // redirected, so a process filling the stderr pipe would deadlock. We now drain both
        // concurrently. Point the "ffmpeg" executable at a present shell so a real process
        // starts and writes to stderr; the call must complete (not hang) and return null.
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings
        {
            FfmpegPath = isWindows ? "cmd" : "sh"
        });
        source.Open();

        var capture = source.CaptureFrameAsync();
        var winner = await Task.WhenAny(capture, Task.Delay(TimeSpan.FromSeconds(20)));

        // The regression is a hang: with stderr redirected but not drained, a chatty process
        // blocks and the await never returns. Completing at all is the assertion.
        winner.Should().Be(capture, "reading stdout+stderr concurrently must not deadlock");
        await capture; // must not throw / must be already completed
    }

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var act = () => new FfmpegCameraSource(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Open_SetsIsOpen()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());

        source.Open();

        source.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Close_ClearsIsOpen()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());
        source.Open();

        source.Close();

        source.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenNotOpen_ReturnsNull()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());

        var frame = await source.CaptureFrameAsync();

        frame.Should().BeNull();
    }

    [Fact]
    public void Dispose_ClosesCamera()
    {
        var source = new FfmpegCameraSource(new FfmpegCameraSettings());
        source.Open();

        source.Dispose();

        source.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Name_ReturnsSettingsName()
    {
        var settings = new FfmpegCameraSettings { Name = "Test Camera" };
        using var source = new FfmpegCameraSource(settings);

        source.Name.Should().Be("Test Camera");
    }

    // --- BuildArguments (CR-M044: per-argument tokens for ArgumentList, no interpolation/injection) ---

    [Fact]
    public void BuildArguments_ProducesExpectedTokenSequence()
    {
        var settings = new FfmpegCameraSettings { JpegQuality = 5 };

        var args = FfmpegCameraSource.BuildArguments(settings, "v4l2", "/dev/video0", "640x480");

        args.Should().Equal(
            "-f", "v4l2",
            "-video_size", "640x480",
            "-i", "/dev/video0",
            "-frames:v", "1",
            "-q:v", "5",
            "-f", "mjpeg",
            "pipe:1");
    }

    [Fact]
    public void BuildArguments_DeviceWithSpaces_IsASingleUnquotedToken()
    {
        // A dshow device name with a space must be one argument (ArgumentList quotes it at launch) —
        // not split, and not carrying embedded shell quotes that ffmpeg would receive literally.
        var settings = new FfmpegCameraSettings();

        var args = FfmpegCameraSource.BuildArguments(settings, "dshow", "video=Integrated Camera", "1280x720");

        var i = args.IndexOf("-i");
        i.Should().BeGreaterThanOrEqualTo(0);
        args[i + 1].Should().Be("video=Integrated Camera");
        args[i + 1].Should().NotContain("\"", "the token must not carry shell-style quotes");
    }

    [Fact]
    public void BuildArguments_DangerousDevicePath_StaysOneToken_NoInjection()
    {
        // An attacker-influenced value cannot inject extra ffmpeg arguments: it remains a single token.
        var settings = new FfmpegCameraSettings();
        var malicious = "/dev/video0 -y /etc/passwd";

        var args = FfmpegCameraSource.BuildArguments(settings, "v4l2", malicious, "640x480");

        var i = args.IndexOf("-i");
        args[i + 1].Should().Be(malicious, "the whole value is one argument, never re-tokenized");
    }
}
