using System;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// A decoded IR command with protocol-specific fields.
    /// </summary>
    public sealed class IrCommand
    {
        /// <summary>
        /// Protocol name (e.g., "NEC", "Samsung", "RC5", "Raw").
        /// </summary>
        public string Protocol { get; set; } = string.Empty;

        /// <summary>
        /// Device/address byte(s). For NEC: 8-bit address. For extended NEC: 16-bit address.
        /// </summary>
        public uint Address { get; set; }

        /// <summary>
        /// Command byte. For NEC: 8-bit command.
        /// </summary>
        public uint Command { get; set; }

        /// <summary>
        /// Full raw code value (all bits). For NEC: 32-bit frame (address + ~address + command + ~command).
        /// </summary>
        public ulong RawCode { get; set; }

        /// <summary>
        /// Whether this is a repeat frame (e.g., NEC repeat code when button is held).
        /// </summary>
        public bool IsRepeat { get; set; }

        /// <summary>
        /// Number of data bits in the protocol frame.
        /// </summary>
        public int BitCount { get; set; }

        /// <summary>
        /// Optional toggle bit (used by RC5/RC6).
        /// </summary>
        public int Toggle { get; set; }

        public override string ToString()
        {
            if (IsRepeat)
            {
                return $"{Protocol} REPEAT";
            }
            return $"{Protocol} Address=0x{Address:X} Command=0x{Command:X} Raw=0x{RawCode:X}";
        }
    }
}
