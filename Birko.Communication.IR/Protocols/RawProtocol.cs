using System;

namespace Birko.Communication.IR.Protocols
{
    /// <summary>
    /// Raw/learning protocol — captures and replays raw IR timings without decoding.
    /// Used for unknown remotes or protocols not yet implemented.
    /// </summary>
    public sealed class RawProtocol : IIrProtocol
    {
        public string Name => "Raw";

        /// <summary>
        /// Encode a command — for raw protocol, the RawCode field is ignored.
        /// Use the timing stored from a previous capture directly via InfraredPort.SendRawAsync().
        /// </summary>
        public IrTiming Encode(IrCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            // Raw protocol doesn't encode from address/command.
            // Callers should use SendRawAsync with captured IrTiming directly.
            throw new NotSupportedException(
                "Raw protocol does not encode from address/command. Use InfraredPort.SendRawAsync() with captured IrTiming.");
        }

        /// <summary>
        /// Decode always succeeds — wraps the raw timing data as an IrCommand.
        /// This is a catch-all protocol for learning mode.
        /// </summary>
        public IrCommand? Decode(IrTiming timing)
        {
            if (timing == null || timing.Durations.Length == 0)
            {
                return null;
            }

            return new IrCommand
            {
                Protocol = Name,
                Address = 0,
                Command = 0,
                RawCode = ComputeHash(timing.Durations),
                BitCount = timing.Durations.Length
            };
        }

        /// <summary>
        /// Compute a simple hash of timing durations for identification.
        /// </summary>
        private static ulong ComputeHash(int[] durations)
        {
            ulong hash = 14695981039346656037UL; // FNV-1a offset basis
            foreach (int d in durations)
            {
                hash ^= (uint)d;
                hash *= 1099511628211UL; // FNV-1a prime
            }
            return hash;
        }
    }
}
