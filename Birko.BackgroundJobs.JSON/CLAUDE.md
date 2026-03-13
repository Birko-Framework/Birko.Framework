# Birko.BackgroundJobs.JSON

## Overview
JSON file-based job queue for Birko.BackgroundJobs. Uses `AsyncJsonStore` from Birko.Data.JSON. Ideal for development, testing, and single-process deployments.

## Project Location
`C:\Source\Birko.BackgroundJobs.JSON\`

## Components

### Models
- `JsonJobDescriptorModel` - Extends `AbstractModel`, uses `[JsonPropertyName]` attributes, maps to/from `JobDescriptor`

### Core
- `JsonJobQueue` - `IJobQueue` implementation using `AsyncJsonStore<JsonJobDescriptorModel>`
- `JsonJobQueueSchema` - Static utility for file creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy)
- Birko.Data (AbstractModel, OrderBy, Settings)
- Birko.Data.JSON (AsyncJsonStore)
- System.Text.Json

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.Stores.Settings` (basic Location + Name)
- No external database dependencies — stores jobs as JSON file on disk
