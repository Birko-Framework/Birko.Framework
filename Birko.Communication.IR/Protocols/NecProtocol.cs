using System;
using System.Collections.Generic;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// NEC infrared protocol encoder/decoder.
    /// Standard NEC: 8-bit address + 8-bit command (32 bits total with complements).
    /// Extended NEC: 16-bit address + 8-bit command (no address complement).
    /// Timing: 38 kHz carrier, 562.5 μs unit.
    ///   Leader:    9000 μs mark + 4500 μs space
    ///   Bit '0':   562 μs mark + 562 μs space
    ///   Bit '1':   562 μs mark + 1687 μs space
    ///   Stop:      562 μs mark
    ///   Repeat:    9000 μs mark + 2250 μs space + 562 μs mark (108 ms period)
    /// </summary>
    public sealed class NecProtocol : IIrProtocol
    {
        private const int LeaderMark = 9000;
        private const int LeaderSpace = 4500;
        private const int RepeatSpace = 2250;
        private const int BitMark = 562;
        private const int ZeroSpace = 562;
        private const int OneSpace = 1687;
        private const int Tolerance = 200;

        public string Name => "NEC";

        /// <summary>
        /// If true, use extended NEC (16-bit address, no complement).
        /// </summary>
        public bool ExtendedMode { get; set; }

        public IrTiming Encode(IrCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (command.IsRepeat)
            {
                return EncodeRepeat();
            }

            uint frame;
            if (ExtendedMode)
            {
                // Extended NEC: address(16) + command(8) + ~command(8)
                uint addr = command.Address & 0xFFFF;
                uint cmd = command.Command & 0xFF;
                uint cmdInv = (~cmd) & 0xFF;
                frame = (addr) | (cmd << 16) | (cmdInv << 24);
            }
            else
            {
                // Standard NEC: address(8) + ~address(8) + command(8) + ~command(8)
                uint addr = command.Address & 0xFF;
                uint addrInv = (~addr) & 0xFF;
                uint cmd = command.Command & 0xFF;
                uint cmdInv = (~cmd) & 0xFF;
                frame = addr | (addrInv << 8) | (cmd << 16) | (cmdInv << 24);
            }

            var durations = new List<int>();

            // Leader
            durations.Add(LeaderMark);
            durations.Add(LeaderSpace);

            // 32 data bits (LSB first)
            for (int i = 0; i < 32; i++)
            {
                durations.Add(BitMark);
                bool bit = ((frame >> i) & 1) == 1;
                durations.Add(bit ? OneSpace : ZeroSpace);
            }

            // Stop bit
            durations.Add(BitMark);

            return new IrTiming(durations.ToArray(), 38000)
            {
                RepeatGapUs = 108000
            };
        }

        public IrCommand? Decode(IrTiming timing)
        {
            if (timing == null || timing.Durations.Length < 3)
            {
                return null;
            }

            var d = timing.Durations;
            int idx = 0;

            // Check leader mark
            if (!InRange(d[idx], LeaderMark))
            {
                return null;
            }
            idx++;

            // Repeat code: leader mark + 2250 space + stop mark
            if (d.Length >= 3 && d.Length <= 5 && InRange(d[idx], RepeatSpace))
            {
                return new IrCommand
                {
                    Protocol = Name,
                    IsRepeat = true,
                    BitCount = 0
                };
            }

            // Leader space
            if (!InRange(d[idx], LeaderSpace))
            {
                return null;
            }
            idx++;

            // Need 32 bits (64 durations) + stop mark = at least 67 durations total
            if (d.Length < idx + 65)
            {
                return null;
            }

            // Decode 32 bits (LSB first)
            uint frame = 0;
            for (int bit = 0; bit < 32; bit++)
            {
                if (!InRange(d[idx], BitMark))
                {
                    return null;
                }
                idx++;

                if (InRange(d[idx], OneSpace))
                {
                    frame |= (1u << bit);
                }
                else if (!InRange(d[idx], ZeroSpace))
                {
                    return null;
                }
                idx++;
            }

            // Extract fields
            uint addrLow = frame & 0xFF;
            uint addrHigh = (frame >> 8) & 0xFF;
            uint cmd = (frame >> 16) & 0xFF;
            uint cmdInv = (frame >> 24) & 0xFF;

            // Validate command complement
            if ((cmd ^ cmdInv) != 0xFF)
            {
                return null;
            }

            uint address;
            bool isExtended;

            // Check if address has valid complement (standard) or not (extended)
            if ((addrLow ^ addrHigh) == 0xFF)
            {
                address = addrLow;
                isExtended = false;
            }
            else
            {
                address = addrLow | (addrHigh << 8);
                isExtended = true;
            }

            return new IrCommand
            {
                Protocol = isExtended ? "NEC-Ext" : Name,
                Address = address,
                Command = cmd,
                RawCode = frame,
                BitCount = 32,
                IsRepeat = false
            };
        }

        private IrTiming EncodeRepeat()
        {
            return new IrTiming(new[] { LeaderMark, RepeatSpace, BitMark }, 38000)
            {
                RepeatGapUs = 108000
            };
        }

        private static bool InRange(int actual, int expected)
        {
            return Math.Abs(actual - expected) <= Tolerance;
        }
    }
}
