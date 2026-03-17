using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Storage.AzureBlob;

/// <summary>
/// Generates SAS (Shared Access Signature) tokens for Azure Blob Storage presigned URLs.
/// Uses the storage account key for HMAC-SHA256 signing (User Delegation SAS requires AAD, not supported here).
/// </summary>
internal static class AzureBlobPresignedUrlProvider
{
    /// <summary>
    /// Generates a Service SAS URI for a blob using the account key.
    /// </summary>
    /// <param name="accountUri">Storage account base URI (e.g., "https://myaccount.blob.core.windows.net").</param>
    /// <param name="containerName">Container name.</param>
    /// <param name="blobPath">Blob path (forward-slash key).</param>
    /// <param name="accountName">Storage account name.</param>
    /// <param name="accountKey">Storage account key (Base64-encoded).</param>
    /// <param name="permissions">SAS permissions string (e.g., "r" for read, "w" for write).</param>
    /// <param name="expiry">Expiration time.</param>
    /// <param name="contentDisposition">Optional Content-Disposition header override.</param>
    /// <param name="contentType">Optional Content-Type header constraint.</param>
    /// <returns>Full presigned URI with SAS token.</returns>
    public static Uri GenerateSasUri(
        string accountUri,
        string containerName,
        string blobPath,
        string accountName,
        string accountKey,
        string permissions,
        DateTimeOffset expiry,
        string? contentDisposition = null,
        string? contentType = null)
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-5); // Clock skew tolerance
        var version = "2023-11-03";

        var canonicalResource = $"/blob/{accountName}/{containerName}/{blobPath}";

        // StringToSign for Service SAS (Blob):
        // permissions + \n + start + \n + expiry + \n + canonicalResource + \n +
        // identifier + \n + IP + \n + protocol + \n + version + \n +
        // resource + \n + snapshot + \n + encryptionScope + \n +
        // rscc + \n + rscd + \n + rsce + \n + rscl + \n + rsct
        var stringToSign = string.Join("\n",
            permissions,
            FormatTime(start),
            FormatTime(expiry),
            canonicalResource,
            string.Empty, // signed identifier
            string.Empty, // IP range
            "https",      // protocol
            version,
            "b",          // resource type: blob
            string.Empty, // snapshot
            string.Empty, // encryption scope
            string.Empty, // rscc (Cache-Control)
            contentDisposition ?? string.Empty, // rscd (Content-Disposition)
            string.Empty, // rsce (Content-Encoding)
            string.Empty, // rscl (Content-Language)
            contentType ?? string.Empty  // rsct (Content-Type)
        );

        var signature = ComputeHmacSha256(accountKey, stringToSign);

        var sb = new StringBuilder();
        sb.Append(accountUri.TrimEnd('/'));
        sb.Append('/');
        sb.Append(containerName);
        sb.Append('/');
        sb.Append(blobPath);
        sb.Append("?sv=").Append(Uri.EscapeDataString(version));
        sb.Append("&sp=").Append(Uri.EscapeDataString(permissions));
        sb.Append("&st=").Append(Uri.EscapeDataString(FormatTime(start)));
        sb.Append("&se=").Append(Uri.EscapeDataString(FormatTime(expiry)));
        sb.Append("&sr=b");
        sb.Append("&spr=https");

        if (!string.IsNullOrEmpty(contentDisposition))
            sb.Append("&rscd=").Append(Uri.EscapeDataString(contentDisposition));
        if (!string.IsNullOrEmpty(contentType))
            sb.Append("&rsct=").Append(Uri.EscapeDataString(contentType));

        sb.Append("&sig=").Append(Uri.EscapeDataString(signature));

        return new Uri(sb.ToString());
    }

    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static string ComputeHmacSha256(string base64Key, string message)
    {
        var keyBytes = Convert.FromBase64String(base64Key);
        using var hmac = new HMACSHA256(keyBytes);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}
