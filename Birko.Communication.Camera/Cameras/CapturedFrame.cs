using System;

namespace Birko.Communication.Camera.Cameras
{
    /// <summary>
    /// A captured camera frame — JPEG bytes + metadata.
    /// </summary>
    public sealed class CapturedFrame
    {
        /// <summary>
        /// JPEG-encoded image data.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Frame width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Frame height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// When the frame was captured (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// JPEG file size in bytes.
        /// </summary>
        public int SizeBytes => Data.Length;

        public CapturedFrame(byte[] data, int width, int height, DateTime timestamp)
        {
            Data = data;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }
    }
}
