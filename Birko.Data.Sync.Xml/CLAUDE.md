# Birko.Data.Sync.Xml

XML-based sync knowledge store for Birko Framework.

## Purpose

Provides XML file-based storage for sync knowledge tracking, enabling synchronization scenarios with XML persistence as an alternative to JSON, SQL, or NoSQL stores.

## Architecture

### Models
- **XmlSyncKnowledgeItem** - XML-serializable sync knowledge model implementing ISyncKnowledgeItem

### Stores
- **AsyncXmlSyncKnowledgeStore** - Async XML file-based implementation of IAsyncSyncKnowledgeItemStore

## Key Features

- **XML Serialization**: Uses System.Xml.Serialization attributes for explicit control
- **File-Based Storage**: Each entity stored as separate XML file in configured directory
- **Thread-Safe Operations**: Inherits thread safety from AsyncXmlStore base
- **Full Async Support**: All operations are async for non-blocking I/O

## Implementation Notes

- Follows same pattern as Birko.Data.Sync.Json but with XML serialization
- XmlSyncKnowledgeItem uses XmlElement attributes for serialization control
- Store inherits from AsyncXmlStore<XmlSyncKnowledgeItem> for XML file operations
- Implements IAsyncSyncKnowledgeItemStore for sync knowledge tracking

## Dependencies

- Birko.Data.Sync (contracts)
- Birko.Data.XML (XML store base classes)
