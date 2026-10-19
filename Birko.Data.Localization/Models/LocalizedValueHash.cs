using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Data.Localization.Models;

/// <summary>
/// The one producer of <see cref="IEntityTranslation.ValueHash"/>: lower-case hex SHA-256 of the UTF-8 value, 64
/// characters (TASK-681).
/// </summary>
/// <remarks>
/// Lookups by an exact translated value (a slug) select rows by this hash, which can be indexed at a fixed, short
/// length, and confirm <see cref="IEntityTranslation.Value"/> in memory. <c>Value</c> itself cannot be indexed without
/// capping every translation (an index key bounds the column to 255 characters on MySQL and SQL Server, rule 37).
/// </remarks>
public static class LocalizedValueHash
{
    public static string Of(string? value)
        => value == null ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
