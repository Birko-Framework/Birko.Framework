# Birko.Data.Sync.Xml

XML-based sync knowledge store for Birko Framework.

## Overview

Provides XML file-based storage for sync knowledge tracking, enabling synchronization scenarios with XML persistence.

## Features

- **XML Serialization**: Uses System.Xml.Serialization for cross-platform compatibility
- **Sync Knowledge Tracking**: Tracks synchronization state across scopes
- **Async/Await Support**: Fully async API for non-blocking operations
- **Thread-Safe**: Safe for concurrent operations

## Usage

```csharp
using Birko.Data.Sync.Xml.Stores;
using Birko.Data.Sync.Xml.Models;

var store = new AsyncXmlSyncKnowledgeStore(new XmlSettings
{
    Location = "./data/sync"
});

// Get last sync time
DateTime? lastSync = await store.GetLastSyncTimeAsync("Products", CancellationToken.None);

// Create sync knowledge item
var item = store.CreateKnowledgeItem(
    guid: Guid.Parse("..."),
    localItemHash: "hash123",
    remoteItemHash: "hash456",
    new SyncOptions { Scope = "Products" }
);
```

## Dependencies

- Birko.Data.Sync
- Birko.Data.XML
