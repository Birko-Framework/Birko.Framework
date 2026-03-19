# Birko.Communication.NFC

## Overview
NFC/RFID tag communication with pluggable transports (Serial, HTTP, HID) and protocol handlers (ISO 14443A, NDEF).

## Project Location
`C:\Source\Birko.Communication.NFC\`

## Components

### Models
- **Models/NfcTagType.cs** — Tag technology enum (ISO14443A/B, MIFARE Classic/Ultralight/DESFire, NTAG, FeliCa, EM4100, HID Prox)
- **Models/NfcTagData.cs** — Tag data: UID, type, SAK, ATQA, NDEF records, payload, metadata
- **Models/NdefRecord.cs** — NDEF record with TNF, type, payload. Helpers: GetUri(), GetText(). NdefTnf enum

### Ports
- **Ports/NfcReaderSettings.cs** — Extends PortSettings: transport type, connection string, timeouts, polling interval, auto-NDEF, repeat reads
- **Ports/NfcReaderPort.cs** — Main port extending AbstractPort: wraps INfcTransport + INfcProtocol list, fires OnTagDetected/OnTagRemoved events

### Transports
- **Transports/INfcTransport.cs** — Interface: Connect, Disconnect, ReadTagAsync, StartPollingAsync, StopPollingAsync, TransceiveAsync, TagDetected/TagRemoved events
- **Transports/SerialNfcTransport.cs** — UART reader (PN532 protocol: InListPassiveTarget, response parsing, SAK-based type detection)
- **Transports/HttpNfcTransport.cs** — REST API reader (GET /api/nfc/tag, POST /api/nfc/apdu, GET /api/nfc/status)
- **Transports/HidNfcTransport.cs** — HID keyboard emulation (FeedInputAsync for keystroke capture, hex/decimal UID parsing)

### Protocols
- **Protocols/INfcProtocol.cs** — Interface: Name, CanHandle(tag), Parse(tag, rawData)
- **Protocols/Iso14443AProtocol.cs** — SAK-based tag classification (MIFARE Classic/Ultralight/DESFire/NTAG), ATQA/UID metadata
- **Protocols/NdefProtocol.cs** — NDEF message parser: TLV wrapper, record header flags (MB/ME/CF/SR/IL), TNF, URI prefix decoding, text language extraction

## Dependencies
- Birko.Communication (AbstractPort, PortSettings, IPort)
- System.IO.Ports (SerialNfcTransport)
- System.Net.Http (HttpNfcTransport)
- System.Text.Json (HttpNfcTransport)

## Maintenance
- When adding new transports, implement INfcTransport and add to .projitems
- When adding new protocols, implement INfcProtocol and add to .projitems
- Update README.md with new transport/protocol documentation
- Update this CLAUDE.md with new components
