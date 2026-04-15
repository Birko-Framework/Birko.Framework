# Birko.BackgroundJobs.XML

## Overview
XML file-based job queue for Birko.BackgroundJobs. Uses `AsyncXmlStore` from Birko.Data.XML. Ideal for development, testing, single-process deployments, and scenarios where a human-readable XML audit trail is preferred over JSON.

## Project Location
`C:\Source\Birko.BackgroundJobs.XML\`

## Components

### Models
- `XmlJobDescriptorModel` — Extends `AbstractModel`, uses `[XmlRoot]`/`[XmlElement]` attributes, maps to/from `JobDescriptor`. Nullable `DateTime?` fields use `IsNullable = true` for proper xsi:nil handling.
- `SerializableMetadata` / `MetadataEntry` — XML-friendly wrapper for the job metadata dictionary (System.Xml.Serialization does not support `Dictionary<TKey, TValue>` directly).

### Core
- `XmlJobQueue` — `IJobQueue` implementation using `AsyncXmlStore<XmlJobDescriptorModel>`
- `XmlJobQueueSchema` — Static utility for file creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy, JobStatus)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy, Settings)
- Birko.Data.XML (AsyncXmlStore)
- Birko.Serialization (ISerializer) + Birko.Serialization.Xml (SystemXmlSerializer) for metadata serialization
- Birko.Time.Abstractions (IDateTimeProvider)
- System.Xml.Serialization

## Maintenance
- Keep in sync with `IJobQueue` interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Configuration.Settings` (via Birko.Data.Stores), basic Location + Name
- No external database dependencies — stores jobs as XML file on disk
- When adding new fields to `JobDescriptor`, update `XmlJobDescriptorModel` (property + `[XmlElement]` attribute + mapping in `ToDescriptor`/`LoadFrom`)
- If extending metadata beyond string-to-string, update `SerializableMetadata` accordingly — XML serialization is stricter than JSON
