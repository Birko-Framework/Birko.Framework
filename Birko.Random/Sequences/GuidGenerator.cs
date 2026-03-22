using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Birko.Random;

/// <summary>
/// Generates GUIDs in v4 (random) and v7 (time-ordered) formats.
/// V7 GUIDs are sortable by creation time, ideal for database primary keys.
/// </summary>
public static class GuidGenerator
{
    /// <summary>
    /// Generates a random UUID v4.
    /// </summary>
    public static Guid NewGuidV4()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        // Set version 4 (0100 in bits 48-51)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        // Set variant 1 (10xx in bits 64-65)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Generates a time-ordered UUID v7 (RFC 9562).
    /// First 48 bits are Unix timestamp in milliseconds, remaining bits are random.
    /// </summary>
    public static Guid NewGuidV7()
    {
        return NewGuidV7(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Generates a time-ordered UUID v7 for the specified timestamp.
    /// </summary>
    public static Guid NewGuidV7(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        long unixMs = timestamp.ToUnixTimeMilliseconds();

        // First 48 bits: Unix timestamp in milliseconds (big-endian)
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        // Set version 7 (0111 in bits 48-51)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // Set variant 1 (10xx in bits 64-65)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
