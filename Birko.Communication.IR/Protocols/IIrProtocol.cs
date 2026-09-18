using System;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// Encodes IR commands to raw timings and decodes raw timings back to commands.
    /// </summary>
    public interface IIrProtocol
    {
        /// <summary>
        /// Protocol name (e.g., "NEC", "Samsung", "RC5").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Encode a command into raw IR timings for transmission.
        /// </summary>
        IrTiming Encode(IrCommand command);

        /// <summary>
        /// Attempt to decode raw IR timings into a command.
        /// Returns null if the timings do not match this protocol.
        /// </summary>
        IrCommand? Decode(IrTiming timing);
    }
}
