using System;

namespace Birko.Communication.IR.Devices
{
    /// <summary>
    /// A named IR command within a device profile.
    /// </summary>
    public sealed class DeviceCommand
    {
        /// <summary>
        /// Human-readable command name (e.g., "PowerOn", "VolumeUp", "TempDown").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of what this command does.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Device address byte(s) for this command.
        /// </summary>
        public uint Address { get; set; }

        /// <summary>
        /// Command code byte(s).
        /// </summary>
        public uint CommandCode { get; set; }

        /// <summary>
        /// Optional: full raw data for protocols that need more than address+command
        /// (e.g., Samsung AC sends 14-byte frames with mode/temp/fan encoded).
        /// </summary>
        public byte[]? ExtendedData { get; set; }
    }
}
