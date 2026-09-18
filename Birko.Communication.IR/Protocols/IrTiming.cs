using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// Raw IR timing data — alternating mark (IR on) and space (IR off) durations in microseconds.
    /// Index 0 is always a mark, index 1 is a space, etc.
    /// </summary>
    public sealed class IrTiming
    {
        /// <summary>
        /// Carrier frequency in Hz (typically 38000 for consumer IR).
        /// </summary>
        public int CarrierFrequencyHz { get; set; } = 38000;

        /// <summary>
        /// Alternating mark/space durations in microseconds.
        /// Even indices = mark (IR on), odd indices = space (IR off).
        /// </summary>
        public int[] Durations { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Number of times to repeat the signal (0 = send once, 1 = send twice, etc.).
        /// </summary>
        public int RepeatCount { get; set; } = 0;

        /// <summary>
        /// Gap between repeats in microseconds.
        /// </summary>
        public int RepeatGapUs { get; set; } = 108000;

        public IrTiming()
        {
        }

        public IrTiming(int[] durations, int carrierFrequencyHz = 38000)
        {
            Durations = durations ?? throw new ArgumentNullException(nameof(durations));
            CarrierFrequencyHz = carrierFrequencyHz;
        }

        /// <summary>
        /// Returns the duration in microseconds of a <b>single pass</b> of the signal — the sum of
        /// <see cref="Durations"/> only. This deliberately excludes <c>RepeatCount</c> and
        /// <c>RepeatGapUs</c>; multiply/add those yourself if you need the full repeated duration (CR-L064).
        /// </summary>
        public long TotalDurationUs()
        {
            long total = 0;
            for (int i = 0; i < Durations.Length; i++)
            {
                total += Durations[i];
            }
            return total;
        }

        /// <summary>
        /// Converts to Pronto hex format (raw learned code).
        /// </summary>
        public string ToProntoHex()
        {
            // Pronto format: frequency code, seq1 length, seq2 length, pairs...
            // Frequency code = 1000000 / (carrierHz * 0.241246)
            double prontoFreq = 1000000.0 / (CarrierFrequencyHz * 0.241246);
            int freqCode = (int)Math.Round(prontoFreq);

            var pairs = new List<int>();
            double prontoPeriod = 1000000.0 / (freqCode * 0.241246);

            for (int i = 0; i < Durations.Length; i++)
            {
                pairs.Add((int)Math.Round(Durations[i] / prontoPeriod));
            }

            // Pad to even count
            if (pairs.Count % 2 != 0)
            {
                pairs.Add(0);
            }

            int burstPairCount = pairs.Count / 2;

            var parts = new List<string>
            {
                "0000",
                freqCode.ToString("X4"),
                "0000",
                burstPairCount.ToString("X4")
            };

            foreach (var p in pairs)
            {
                parts.Add(p.ToString("X4"));
            }

            return string.Join(" ", parts);
        }
    }
}
