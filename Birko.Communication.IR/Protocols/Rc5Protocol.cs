using System;
using System.Collections.Generic;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// Philips RC5 infrared protocol encoder/decoder.
    /// Timing: 36 kHz carrier, 889 μs half-bit period (Manchester encoding).
    ///   Bit '0': 889 μs mark + 889 μs space (high-to-low transition)
    ///   Bit '1': 889 μs space + 889 μs mark (low-to-high transition)
    /// Frame (14 bits): S1(1) + S2(1) + Toggle(1) + Address(5) + Command(6)
    ///   S1 = 1 (start bit), S2 = 1 for RC5 (0 extends command to 7 bits for RC5X).
    /// </summary>
    public sealed class Rc5Protocol : IIrProtocol
    {
        private const int HalfBit = 889;
        private const int Tolerance = 200;
        private const int CarrierHz = 36000;

        public string Name => "RC5";

        public IrTiming Encode(IrCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            uint addr = command.Address & 0x1F;  // 5 bits
            uint cmd = command.Command & 0x3F;    // 6 bits
            int toggle = command.Toggle & 1;

            // Build 14-bit frame: S1=1, S2=1, Toggle, Address(5), Command(6)
            int frame = (1 << 13) | (1 << 12) | (toggle << 11);
            frame |= (int)(addr << 6);
            frame |= (int)cmd;

            // Manchester encode — each bit becomes two half-bit periods
            var durations = new List<int>();
            bool lastLevel = false; // start from space (IR off)

            for (int i = 13; i >= 0; i--)
            {
                bool bit = ((frame >> i) & 1) == 1;

                // RC5 Manchester: bit 1 = space then mark, bit 0 = mark then space
                bool firstHalf = !bit;  // mark for 0, space for 1
                bool secondHalf = bit;  // space for 0, mark for 1

                AddTransition(durations, ref lastLevel, firstHalf, HalfBit);
                AddTransition(durations, ref lastLevel, secondHalf, HalfBit);
            }

            return new IrTiming(durations.ToArray(), CarrierHz);
        }

        public IrCommand? Decode(IrTiming timing)
        {
            if (timing == null || timing.Durations.Length < 10)
            {
                return null;
            }

            // Convert raw durations to half-bit slots
            var halfBits = new List<bool>();
            bool isMark = true; // first duration is always mark

            foreach (int duration in timing.Durations)
            {
                int slots = (int)Math.Round((double)duration / HalfBit);
                if (slots < 1 || slots > 3)
                {
                    return null;
                }

                for (int s = 0; s < slots; s++)
                {
                    halfBits.Add(isMark);
                }
                isMark = !isMark;
            }

            // Decode Manchester: pairs of half-bits -> data bits
            // RC5: mark→space = 0, space→mark = 1
            if (halfBits.Count < 28) // 14 bits * 2
            {
                return null;
            }

            int frame = 0;
            for (int bit = 0; bit < 14; bit++)
            {
                int idx = bit * 2;
                if (idx + 1 >= halfBits.Count)
                {
                    return null;
                }

                bool first = halfBits[idx];
                bool second = halfBits[idx + 1];

                if (first && !second)
                {
                    // mark→space = 0
                    frame <<= 1;
                }
                else if (!first && second)
                {
                    // space→mark = 1
                    frame = (frame << 1) | 1;
                }
                else
                {
                    return null; // invalid Manchester
                }
            }

            int toggleBit = (frame >> 11) & 1;
            uint address = (uint)((frame >> 6) & 0x1F);
            uint command = (uint)(frame & 0x3F);

            return new IrCommand
            {
                Protocol = Name,
                Address = address,
                Command = command,
                RawCode = (ulong)frame,
                BitCount = 14,
                Toggle = toggleBit
            };
        }

        private static void AddTransition(List<int> durations, ref bool lastLevel, bool newLevel, int period)
        {
            if (durations.Count == 0)
            {
                // First entry: if mark, add directly; if space, add zero-length mark first
                if (newLevel)
                {
                    durations.Add(period);
                }
                else
                {
                    durations.Add(0); // zero mark
                    durations.Add(period);
                }
                lastLevel = newLevel;
                return;
            }

            if (newLevel == lastLevel)
            {
                // Same level — extend the last duration
                durations[durations.Count - 1] += period;
            }
            else
            {
                // Level change — new duration entry
                durations.Add(period);
                lastLevel = newLevel;
            }
        }
    }
}
