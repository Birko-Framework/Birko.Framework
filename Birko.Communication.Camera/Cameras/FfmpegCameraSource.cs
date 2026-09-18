using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.Camera.Cameras
{
    /// <summary>
    /// Camera source using FFmpeg to capture JPEG snapshots.
    /// No NuGet dependencies — shells out to the ffmpeg binary.
    ///
    /// Requires: ffmpeg installed and in PATH.
    ///   Linux:   sudo apt install ffmpeg
    ///   Windows: winget install ffmpeg / choco install ffmpeg
    ///   macOS:   brew install ffmpeg
    /// </summary>
    public class FfmpegCameraSource : ICameraSource
    {
        private readonly FfmpegCameraSettings _settings;

        public string Name => _settings.Name;
        public bool IsOpen { get; private set; }

        public FfmpegCameraSource(FfmpegCameraSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Open()
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>
        /// Captures a single JPEG frame by running ffmpeg and reading stdout.
        /// </summary>
        public async Task<CapturedFrame?> CaptureFrameAsync(CancellationToken ct = default)
        {
            if (!IsOpen) return null;

            var inputFormat = _settings.InputFormat ?? GetDefaultInputFormat();
            var device = _settings.DevicePath ?? GetDefaultDevicePath();
            var resolution = $"{_settings.Width}x{_settings.Height}";

            var psi = new ProcessStartInfo
            {
                FileName = _settings.FfmpegPath ?? "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Pass each token via ArgumentList so ProcessStartInfo handles per-argument quoting. A
            // DevicePath with spaces/quotes (e.g. dshow `video=Integrated Camera`) is then delivered
            // as one argument and cannot be mis-tokenized or used for argument injection (CR-M044).
            foreach (var arg in BuildArguments(_settings, inputFormat, device, resolution))
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();

                using var ms = new MemoryStream();
                // Drain stderr concurrently with stdout. ffmpeg is very verbose on stderr (banner,
                // stream info, progress); if we only read stdout, ffmpeg blocks writing to a full
                // stderr pipe while we block reading stdout — a classic redirected-process deadlock
                // (CR-H019). Discard stderr to Stream.Null and await both before WaitForExit.
                var stderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);
                await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
                await stderrTask;

                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0 || ms.Length == 0)
                    return null;

                return new CapturedFrame(
                    ms.ToArray(),
                    _settings.Width,
                    _settings.Height,
                    DateTime.UtcNow);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception)
            {
                // ffmpeg not found or device not accessible
                return null;
            }
            finally
            {
                // Process.Dispose() does NOT terminate a still-running child, so a cancelled or failed
                // capture would leave an orphaned ffmpeg holding the camera device open (CR-M043).
                // Kill the tree if it survived; on the success path it has already exited (no-op).
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Never started, already exited, or racing exit — nothing to clean up.
                }
            }
        }

        /// <summary>
        /// Builds the ffmpeg argument token list (one element per argument, for
        /// <see cref="ProcessStartInfo.ArgumentList"/>). Extracted for testability (CR-M044).
        /// </summary>
        internal static System.Collections.Generic.List<string> BuildArguments(
            FfmpegCameraSettings settings, string inputFormat, string device, string resolution)
        {
            return new System.Collections.Generic.List<string>
            {
                "-f", inputFormat,
                "-video_size", resolution,
                "-i", device,
                "-frames:v", "1",
                "-q:v", settings.JpegQuality.ToString(),
                "-f", "mjpeg",
                "pipe:1"
            };
        }

        private static string GetDefaultInputFormat()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "v4l2";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "dshow";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "avfoundation";
            return "v4l2";
        }

        private static string GetDefaultDevicePath()
        {
            // No shell-style quoting here — ArgumentList quotes each token, so the raw value
            // (spaces and all) is what ffmpeg receives (CR-M044).
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "/dev/video0";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "video=Integrated Camera";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "0";
            return "/dev/video0";
        }

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>
    /// Configuration for FfmpegCameraSource.
    /// </summary>
    public class FfmpegCameraSettings
    {
        /// <summary>Display name for the camera.</summary>
        public string Name { get; set; } = "USB Camera";

        /// <summary>
        /// Device path. Platform-specific — supply the RAW value; do NOT add shell quoting, the
        /// process launcher quotes each argument (CR-M044):
        ///   Linux: /dev/video0, /dev/video1
        ///   Windows: video=Camera Name (from `ffmpeg -list_devices true -f dshow -i dummy`)
        ///   macOS: 0 (device index)
        /// Null = auto-detect default.
        /// </summary>
        public string? DevicePath { get; set; }

        /// <summary>
        /// FFmpeg input format. Null = auto-detect by OS.
        ///   Linux: v4l2, Windows: dshow, macOS: avfoundation
        /// </summary>
        public string? InputFormat { get; set; }

        /// <summary>Frame width. Default: 640.</summary>
        public int Width { get; set; } = 640;

        /// <summary>Frame height. Default: 480.</summary>
        public int Height { get; set; } = 480;

        private int _jpegQuality = 5;
        /// <summary>JPEG quality (1=best, 31=worst). Values are clamped to [1,31] so an out-of-range
        /// value can't silently fail the ffmpeg capture. Default: 5.</summary>
        public int JpegQuality
        {
            get => _jpegQuality;
            set => _jpegQuality = System.Math.Clamp(value, 1, 31); // CR-L048
        }

        /// <summary>Path to ffmpeg binary. Null = use PATH.</summary>
        public string? FfmpegPath { get; set; }
    }
}
