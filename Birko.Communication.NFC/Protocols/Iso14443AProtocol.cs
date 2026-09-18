using System;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Protocols
{
    /// <summary>
    /// ISO 14443 Type A protocol handler.
    /// Identifies MIFARE Classic, Ultralight, DESFire, and NTAG variants based on SAK/ATQA.
    /// </summary>
    public class Iso14443AProtocol : INfcProtocol
    {
        public string Name => "ISO14443A";

        public bool CanHandle(NfcTagData tag)
        {
            return tag.TagType == NfcTagType.Iso14443A
                || tag.TagType == NfcTagType.MifareClassic
                || tag.TagType == NfcTagType.MifareUltralight
                || tag.TagType == NfcTagType.MifareDESFire
                || tag.TagType == NfcTagType.Ntag;
        }

        public void Parse(NfcTagData tag, byte[] rawData)
        {
            if (tag.Sak.HasValue)
            {
                tag.TagType = ClassifySak(tag.Sak.Value);
                tag.Metadata["SAK"] = $"0x{tag.Sak.Value:X2}";
            }

            if (tag.Atqa != null && tag.Atqa.Length >= 2)
            {
                tag.Metadata["ATQA"] = $"0x{tag.Atqa[0]:X2}{tag.Atqa[1]:X2}";
            }

            tag.Metadata["UIDLength"] = tag.UidBytes.Length.ToString();
            tag.Metadata["UIDType"] = tag.UidBytes.Length switch
            {
                4 => "Single",
                7 => "Double",
                10 => "Triple",
                _ => "Unknown"
            };

            if (rawData.Length > 0)
            {
                tag.Payload = rawData;
            }
        }

        /// <summary>
        /// Classify the tag sub-type based on the SAK (Select Acknowledge) byte.
        /// </summary>
        public static NfcTagType ClassifySak(byte sak)
        {
            // SAK bit analysis per NXP AN10833
            return sak switch
            {
                0x08 => NfcTagType.MifareClassic,  // MIFARE Classic 1K
                0x18 => NfcTagType.MifareClassic,  // MIFARE Classic 4K
                0x09 => NfcTagType.MifareClassic,  // MIFARE Mini
                0x00 => NfcTagType.MifareUltralight, // MIFARE Ultralight / NTAG
                0x04 => NfcTagType.Ntag,            // NTAG with SAK=0x04
                0x20 => NfcTagType.MifareDESFire,   // MIFARE DESFire / ISO 14443-4
                0x28 => NfcTagType.MifareClassic,   // SmartMX with MIFARE Classic 1K emulation
                0x38 => NfcTagType.MifareClassic,   // SmartMX with MIFARE Classic 4K emulation
                _ => NfcTagType.Iso14443A           // Generic ISO 14443A
            };
        }
    }
}
