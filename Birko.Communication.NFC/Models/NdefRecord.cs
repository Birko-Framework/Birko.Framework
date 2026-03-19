using System;
using System.Text;

namespace Birko.Communication.NFC.Models
{
    /// <summary>
    /// NDEF record type name format (TNF).
    /// </summary>
    public enum NdefTnf : byte
    {
        Empty = 0x00,
        WellKnown = 0x01,
        MimeMedia = 0x02,
        AbsoluteUri = 0x03,
        External = 0x04,
        Unknown = 0x05,
        Unchanged = 0x06
    }

    /// <summary>
    /// A single NDEF (NFC Data Exchange Format) record.
    /// </summary>
    public sealed class NdefRecord
    {
        /// <summary>
        /// Type Name Format — indicates how to interpret the Type field.
        /// </summary>
        public NdefTnf Tnf { get; set; } = NdefTnf.Empty;

        /// <summary>
        /// Record type (e.g., "T" for text, "U" for URI, "Sp" for smart poster).
        /// </summary>
        public byte[] Type { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Optional record identifier.
        /// </summary>
        public byte[]? Id { get; set; }

        /// <summary>
        /// Record payload.
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Returns the type field as a UTF-8 string.
        /// </summary>
        public string TypeString => Encoding.UTF8.GetString(Type);

        /// <summary>
        /// For well-known URI records (TNF=0x01, Type="U"), extracts the URI string.
        /// Returns null if this is not a URI record.
        /// </summary>
        public string? GetUri()
        {
            if (Tnf != NdefTnf.WellKnown || Type.Length != 1 || Type[0] != (byte)'U' || Payload.Length < 1)
            {
                return null;
            }

            var prefix = Payload[0] switch
            {
                0x01 => "http://www.",
                0x02 => "https://www.",
                0x03 => "http://",
                0x04 => "https://",
                0x05 => "tel:",
                0x06 => "mailto:",
                _ => ""
            };

            return prefix + Encoding.UTF8.GetString(Payload, 1, Payload.Length - 1);
        }

        /// <summary>
        /// For well-known text records (TNF=0x01, Type="T"), extracts the text and language code.
        /// Returns null if this is not a text record.
        /// </summary>
        public (string Language, string Text)? GetText()
        {
            if (Tnf != NdefTnf.WellKnown || Type.Length != 1 || Type[0] != (byte)'T' || Payload.Length < 1)
            {
                return null;
            }

            byte statusByte = Payload[0];
            int langLen = statusByte & 0x3F;
            bool isUtf16 = (statusByte & 0x80) != 0;

            if (Payload.Length < 1 + langLen)
            {
                return null;
            }

            var language = Encoding.ASCII.GetString(Payload, 1, langLen);
            var encoding = isUtf16 ? Encoding.Unicode : Encoding.UTF8;
            var text = encoding.GetString(Payload, 1 + langLen, Payload.Length - 1 - langLen);

            return (language, text);
        }

        public override string ToString()
        {
            return $"NDEF TNF={Tnf} Type={TypeString} PayloadLength={Payload.Length}";
        }
    }
}
