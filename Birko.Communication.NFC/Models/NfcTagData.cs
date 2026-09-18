using System;
using System.Collections.Generic;

namespace Birko.Communication.NFC.Models
{
    /// <summary>
    /// Data read from an NFC/RFID tag.
    /// </summary>
    public sealed class NfcTagData
    {
        /// <summary>
        /// Tag unique identifier (UID) as hex string (e.g., "04A1B2C3D4E5F6").
        /// </summary>
        public string Uid { get; set; } = string.Empty;

        /// <summary>
        /// Raw UID bytes.
        /// </summary>
        public byte[] UidBytes { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Detected tag technology type.
        /// </summary>
        public NfcTagType TagType { get; set; } = NfcTagType.Unknown;

        /// <summary>
        /// Answer To Select (ATS) for ISO 14443-4 tags, null if not available.
        /// </summary>
        public byte[]? Ats { get; set; }

        /// <summary>
        /// Select Acknowledge (SAK) byte for ISO 14443A tags.
        /// Used to identify tag sub-type (MIFARE Classic = 0x08, DESFire = 0x20, etc.).
        /// </summary>
        public byte? Sak { get; set; }

        /// <summary>
        /// ATQA (Answer To Request Type A) bytes for ISO 14443A tags.
        /// </summary>
        public byte[]? Atqa { get; set; }

        /// <summary>
        /// NDEF records found on the tag (if any).
        /// </summary>
        public List<NdefRecord> NdefRecords { get; set; } = new();

        /// <summary>
        /// Raw payload data read from the tag (sector/block data, file contents, etc.).
        /// </summary>
        public byte[]? Payload { get; set; }

        /// <summary>
        /// Timestamp when the tag was read.
        /// </summary>
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Additional metadata from the reader (e.g., signal strength, reader ID).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        public override string ToString()
        {
            return $"{TagType} UID={Uid}";
        }

        /// <summary>
        /// Returns UID as colon-separated hex string (e.g., "04:A1:B2:C3:D4:E5:F6").
        /// </summary>
        public string GetFormattedUid()
        {
            if (UidBytes.Length == 0)
            {
                return Uid;
            }
            return BitConverter.ToString(UidBytes).Replace("-", ":");
        }
    }
}
