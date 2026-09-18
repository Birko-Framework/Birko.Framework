namespace Birko.Security.NFC
{
    /// <summary>
    /// Configuration for NFC authentication.
    /// </summary>
    public class NfcAuthSettings
    {
        /// <summary>
        /// Whether to issue JWT tokens on successful NFC authentication.
        /// When false, only the NfcAuthResult is returned without a token.
        /// </summary>
        public bool IssueTokens { get; set; } = true;

        /// <summary>
        /// Whether to update LastUsedAt timestamp on each successful authentication.
        /// </summary>
        public bool TrackUsage { get; set; } = true;

        /// <summary>
        /// Whether to check tag expiration dates.
        /// </summary>
        public bool EnforceExpiration { get; set; } = true;

        /// <summary>
        /// Maximum number of tags that can be enrolled per user. 0 = unlimited.
        /// </summary>
        public int MaxTagsPerUser { get; set; } = 5;

        /// <summary>
        /// Whether to normalize tag UIDs to uppercase hex without separators before lookup.
        /// </summary>
        public bool NormalizeUids { get; set; } = true;
    }
}
