using System;
using System.Collections.Generic;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Protocols
{
    /// <summary>
    /// NDEF (NFC Data Exchange Format) protocol handler.
    /// Parses NDEF messages from raw tag data into structured NdefRecord objects.
    /// Supports NFC Forum Type 2 (NTAG/Ultralight) and Type 4 (DESFire) tag layouts.
    /// </summary>
    public class NdefProtocol : INfcProtocol
    {
        public string Name => "NDEF";

        public bool CanHandle(NfcTagData tag)
        {
            return tag.TagType == NfcTagType.MifareUltralight
                || tag.TagType == NfcTagType.Ntag
                || tag.TagType == NfcTagType.MifareDESFire;
        }

        public void Parse(NfcTagData tag, byte[] rawData)
        {
            var records = ParseNdefMessage(rawData);
            tag.NdefRecords.AddRange(records);
            tag.Metadata["NdefRecordCount"] = records.Count.ToString();
        }

        /// <summary>
        /// Parse an NDEF message from raw bytes into a list of records.
        /// </summary>
        public static List<NdefRecord> ParseNdefMessage(byte[] data)
        {
            var records = new List<NdefRecord>();
            int offset = 0;

            // For Type 2 tags, skip TLV wrapper if present
            if (data.Length > 2 && data[0] == 0x03)
            {
                // NDEF Message TLV: Type=0x03, Length, Value
                offset = 1;
                int ndefLength = data[offset++];
                if (ndefLength == 0xFF && offset + 1 < data.Length)
                {
                    // 3-byte length format
                    ndefLength = (data[offset] << 8) | data[offset + 1];
                    offset += 2;
                }
            }

            while (offset < data.Length)
            {
                if (data[offset] == 0xFE) // Terminator TLV
                {
                    break;
                }

                var record = ParseRecord(data, ref offset);
                if (record == null)
                {
                    break;
                }
                records.Add(record);
            }

            return records;
        }

        private static NdefRecord? ParseRecord(byte[] data, ref int offset)
        {
            if (offset >= data.Length)
            {
                return null;
            }

            byte header = data[offset++];
            bool mb = (header & 0x80) != 0; // Message Begin
            bool me = (header & 0x40) != 0; // Message End
            bool cf = (header & 0x20) != 0; // Chunk Flag
            bool sr = (header & 0x10) != 0; // Short Record
            bool il = (header & 0x08) != 0; // ID Length present
            var tnf = (NdefTnf)(header & 0x07);

            if (offset >= data.Length)
            {
                return null;
            }

            byte typeLength = data[offset++];

            int payloadLength;
            if (sr)
            {
                if (offset >= data.Length) return null;
                payloadLength = data[offset++];
            }
            else
            {
                if (offset + 3 >= data.Length) return null;
                payloadLength = (data[offset] << 24) | (data[offset + 1] << 16)
                              | (data[offset + 2] << 8) | data[offset + 3];
                offset += 4;
            }

            byte idLength = 0;
            if (il)
            {
                if (offset >= data.Length) return null;
                idLength = data[offset++];
            }

            if (offset + typeLength > data.Length) return null;
            var type = new byte[typeLength];
            Array.Copy(data, offset, type, 0, typeLength);
            offset += typeLength;

            byte[]? id = null;
            if (idLength > 0)
            {
                if (offset + idLength > data.Length) return null;
                id = new byte[idLength];
                Array.Copy(data, offset, id, 0, idLength);
                offset += idLength;
            }

            if (offset + payloadLength > data.Length) return null;
            var payload = new byte[payloadLength];
            Array.Copy(data, offset, payload, 0, payloadLength);
            offset += payloadLength;

            return new NdefRecord
            {
                Tnf = tnf,
                Type = type,
                Id = id,
                Payload = payload
            };
        }
    }
}
