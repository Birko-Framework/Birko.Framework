using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Protocols
{
    /// <summary>
    /// Protocol handler for parsing and interacting with specific NFC tag types.
    /// </summary>
    public interface INfcProtocol
    {
        /// <summary>
        /// Protocol name (e.g., "ISO14443A", "NDEF").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Check if this protocol can handle the given tag.
        /// </summary>
        bool CanHandle(NfcTagData tag);

        /// <summary>
        /// Parse additional data from the tag (e.g., read NDEF records, decode sector data).
        /// Modifies the tag data in-place (adds NdefRecords, Payload, Metadata).
        /// </summary>
        void Parse(NfcTagData tag, byte[] rawData);
    }
}
