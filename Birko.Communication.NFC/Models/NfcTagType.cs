namespace Birko.Communication.NFC.Models
{
    /// <summary>
    /// NFC/RFID tag technology types.
    /// </summary>
    public enum NfcTagType
    {
        /// <summary>
        /// Unknown or unrecognized tag type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// ISO 14443 Type A — MIFARE Classic, MIFARE Ultralight, NTAG.
        /// </summary>
        Iso14443A = 1,

        /// <summary>
        /// ISO 14443 Type B — banking cards, government IDs.
        /// </summary>
        Iso14443B = 2,

        /// <summary>
        /// ISO 15693 — longer range RFID (ICODE, Tag-it HF-I).
        /// </summary>
        Iso15693 = 3,

        /// <summary>
        /// MIFARE Classic (1K/4K) — proprietary NXP, sector/block structure.
        /// </summary>
        MifareClassic = 10,

        /// <summary>
        /// MIFARE Ultralight — lightweight, no crypto.
        /// </summary>
        MifareUltralight = 11,

        /// <summary>
        /// MIFARE DESFire — AES/3DES mutual authentication, file-based.
        /// </summary>
        MifareDESFire = 12,

        /// <summary>
        /// NTAG213/215/216 — NFC Forum Type 2, NDEF-capable.
        /// </summary>
        Ntag = 13,

        /// <summary>
        /// FeliCa (JIS X 6319-4) — Sony, used in Japan (Suica, PASMO).
        /// </summary>
        FeliCa = 20,

        /// <summary>
        /// EM4100/EM4200 — 125 kHz low-frequency RFID (read-only ID).
        /// </summary>
        Em4100 = 30,

        /// <summary>
        /// HID iCLASS/Prox — access control cards.
        /// </summary>
        HidProx = 31
    }
}
