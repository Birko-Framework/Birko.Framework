using System;
using System.Collections.Generic;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Devices
{
    /// <summary>
    /// A codebook for a specific device model — maps named commands to IR codes.
    /// </summary>
    public interface IDeviceProfile
    {
        /// <summary>
        /// Device manufacturer (e.g., "Samsung", "LG", "Sony").
        /// </summary>
        string Manufacturer { get; }

        /// <summary>
        /// Device model identifier.
        /// </summary>
        string Model { get; }

        /// <summary>
        /// Protocol used by this device.
        /// </summary>
        IIrProtocol Protocol { get; }

        /// <summary>
        /// Get all available command names.
        /// </summary>
        IReadOnlyList<string> GetCommandNames();

        /// <summary>
        /// Get the IR command for a named action (e.g., "PowerOn", "TempUp").
        /// Returns null if the command name is not recognized.
        /// </summary>
        IrCommand? GetCommand(string name);

        /// <summary>
        /// Get the raw IR timing for a named action, ready for transmission.
        /// Returns null if the command name is not recognized.
        /// </summary>
        IrTiming? GetTiming(string name);
    }
}
