using System;

namespace Birko.Random
{
    /// <summary>Fills the supplied buffer with random bytes (e.g. RandomNumberGenerator.Fill or IRandomProvider.NextBytes).</summary>
    internal delegate void SpanByteFill(Span<byte> buffer);

    /// <summary>
    /// CR-L328: shared mask-rejection sampling for NanoId / Token generation — the mask/step computation
    /// and rejection-fill loop were copy-pasted across NanoIdGenerator (RNG + provider) and
    /// TokenGenerator. CR-L327: single-character alphabets are special-cased, because the mask formula
    /// evaluates <c>Math.Log(alphabet.Length - 1)</c> = <c>Math.Log(0)</c> = -Infinity at length 1
    /// (yielding a nonsensical mask); a 1-char alphabet trivially fills with that one character.
    /// Callers validate the alphabet (non-empty) and size (positive) before calling.
    /// </summary>
    internal static class AlphabetSampler
    {
        public static string Sample(string alphabet, int size, SpanByteFill fill)
        {
            if (alphabet.Length == 1)
            {
                return new string(alphabet[0], size);
            }

            // Mask for uniform distribution over the alphabet (power-of-two ceiling minus one).
            int mask = (2 << (int)Math.Floor(Math.Log(alphabet.Length - 1) / Math.Log(2))) - 1;
            int step = (int)Math.Ceiling(1.6 * mask * size / alphabet.Length);

            Span<byte> bytes = stackalloc byte[step];
            Span<char> result = stackalloc char[size];
            int count = 0;

            while (count < size)
            {
                fill(bytes);

                for (int i = 0; i < step && count < size; i++)
                {
                    int index = bytes[i] & mask;
                    if (index < alphabet.Length)
                    {
                        result[count++] = alphabet[index];
                    }
                }
            }

            return new string(result);
        }
    }
}
