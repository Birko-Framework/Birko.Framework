using System;
using System.Collections.Generic;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// Samsung infrared protocol encoder/decoder (32-bit TV remote variant).
    /// Timing: 38 kHz carrier, ~550 μs unit.
    ///   Leader:    4500 μs mark + 4500 μs space
    ///   Bit '0':   550 μs mark + 550 μs space
    ///   Bit '1':   550 μs mark + 1650 μs space
    ///   Stop:      550 μs mark
    /// Frame: address(8) + address(8, repeated) + command(8) + ~command(8)
    /// Note: Samsung repeats the address instead of sending its complement.
    /// </summary>
    public sealed class SamsungProtocol : IIrProtocol
    {
        private const int LeaderMark = 4500;
        private const int LeaderSpace = 4500;
        private const int BitMark = 550;
        private const int ZeroSpace = 550;
        private const int OneSpace = 1650;
        private const int Tolerance = 200;

        public string Name => "Samsung";

        public IrTiming Encode(IrCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            uint addr = command.Address & 0xFF;
            uint cmd = command.Command & 0xFF;
            uint cmdInv = (~cmd) & 0xFF;

            // Samsung: address + address (repeated) + command + ~command
            uint frame = addr | (addr << 8) | (cmd << 16) | (cmdInv << 24);

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

            return new IrTiming(durations.ToArray(), 38000);
        }

        public IrCommand? Decode(IrTiming timing)
        {
            if (timing == null || timing.Durations.Length < 67)
            {
                return null;
            }

            var d = timing.Durations;
            int idx = 0;

            // Leader
            if (!InRange(d[idx], LeaderMark) || !InRange(d[idx + 1], LeaderSpace))
            {
                return null;
            }
            idx += 2;

            // Decode 32 bits
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

            uint addrLow = frame & 0xFF;
            uint addrHigh = (frame >> 8) & 0xFF;
            uint cmd = (frame >> 16) & 0xFF;
            uint cmdInv = (frame >> 24) & 0xFF;

            // Validate: Samsung repeats address, complement on command
            if (addrLow != addrHigh)
            {
                return null;
            }

            if ((cmd ^ cmdInv) != 0xFF)
            {
                return null;
            }

            return new IrCommand
            {
                Protocol = Name,
                Address = addrLow,
                Command = cmd,
                RawCode = frame,
                BitCount = 32
            };
        }

        private static bool InRange(int actual, int expected)
        {
            return Math.Abs(actual - expected) <= Tolerance;
        }
    }
}
