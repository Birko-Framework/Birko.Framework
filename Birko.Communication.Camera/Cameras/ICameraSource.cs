using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.Camera.Cameras
{
    /// <summary>
    /// Abstraction for capturing still frames from a camera device.
    /// Implementations: FfmpegCameraSource (lightweight, no NuGet),
    /// future: OpenCvCameraSource (real-time, NuGet dependency).
    /// </summary>
    public interface ICameraSource : IDisposable
    {
        /// <summary>
        /// Human-readable source name (e.g. "/dev/video0", "USB Camera").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether the source is ready to capture.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Opens the camera source. Must be called before CaptureFrameAsync.
        /// </summary>
        void Open();

        /// <summary>
        /// Closes the camera source and releases resources.
        /// </summary>
        void Close();

        /// <summary>
        /// Captures a single JPEG frame from the camera.
        /// </summary>
        Task<CapturedFrame?> CaptureFrameAsync(CancellationToken ct = default);
    }
}
