using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Sync;
using Birko.Data.Sync.Models;

namespace Birko.Framework.Examples.Data
{
    /// <summary>
    /// Examples demonstrating the Birko.Data.Sync synchronization framework.
    /// SyncProvider requires IBulkStore implementations for local/remote stores
    /// and an ISyncKnowledgeItemStore for tracking sync state.
    /// </summary>
    public static class SyncExamples
    {
        /// <summary>
        /// Basic sync: configure SyncOptions for download and execute sync.
        /// </summary>
        public static Task RunBasicSyncExample()
        {
            ExampleOutput.WriteLine("=== Basic Sync Example ===\n");

            // SyncProvider requires three stores:
            //   localStore  - IBulkStore<T> for local data
            //   remoteStore - IBulkStore<T> for remote data
            //   knowledgeStore - ISyncKnowledgeItemStore<TKnowledge> for tracking sync state
            //
            // var provider = new SyncProvider<MyBulkStore, ExampleDocument, MySyncKnowledge>(
            //     localStore, remoteStore, knowledgeStore);

            // Configure sync options for downloading from remote to local
            var options = new SyncOptions
            {
                Direction = SyncDirection.Download,
                Scope = "Documents",
                BatchSize = 50,
                ConflictPolicy = ConflictResolutionPolicy.RemoteWins,
                OnProgress = progress =>
                {
                    ExampleOutput.WriteLine($"  Progress: {progress.ProcessedItems}/{progress.TotalItems} items");
                },
                OnError = error =>
                {
                    ExampleOutput.WriteLine($"  Error: {error.Message} - {error.Details}");
                }
            };

            ExampleOutput.WriteLine($"Direction: {options.Direction}");
            ExampleOutput.WriteLine($"Scope: {options.Scope}");
            ExampleOutput.WriteLine($"Batch Size: {options.BatchSize}");
            ExampleOutput.WriteLine($"Conflict Policy: {options.ConflictPolicy}");

            // Execute sync (requires running stores):
            // SyncResult result = provider.Sync(options);
            // ExampleOutput.WriteLine($"Success: {result.Success}");
            // ExampleOutput.WriteLine($"Created: {result.Created}, Updated: {result.Updated}, Deleted: {result.Deleted}");
            // ExampleOutput.WriteLine($"Duration: {result.Duration.TotalMilliseconds}ms");

            ExampleOutput.WriteLine("\n[Sync execution requires configured IBulkStore instances]");
            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Preview sync changes before executing.
        /// </summary>
        public static Task RunPreviewSyncExample()
        {
            ExampleOutput.WriteLine("=== Preview Sync Example ===\n");

            var options = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.NewestWins
            };

            // Preview shows what would happen without making changes:
            // SyncPreview preview = provider.Preview(options);
            // ExampleOutput.WriteLine($"Items to create: {preview.ToCreate}");
            // ExampleOutput.WriteLine($"Items to update: {preview.ToUpdate}");
            // ExampleOutput.WriteLine($"Items to delete: {preview.ToDelete}");
            // ExampleOutput.WriteLine($"Conflicts: {preview.Conflicts}");
            // ExampleOutput.WriteLine($"Skipped: {preview.Skipped}");
            //
            // // Inspect individual items planned for sync
            // foreach (var item in preview.Items)
            // {
            //     ExampleOutput.WriteLine($"  {item.Guid}: {item.Action}");
            // }
            //
            // // If preview looks good, execute the sync
            // if (preview.Conflicts == 0)
            // {
            //     SyncResult result = provider.Sync(options);
            // }

            ExampleOutput.WriteLine("SyncPreview properties:");
            ExampleOutput.WriteLine("  - ToCreate: number of items to be created");
            ExampleOutput.WriteLine("  - ToUpdate: number of items to be updated");
            ExampleOutput.WriteLine("  - ToDelete: number of items to be deleted");
            ExampleOutput.WriteLine("  - Conflicts: number of conflicts detected");
            ExampleOutput.WriteLine("  - Items: detailed list of SyncItemPreview entries");
            ExampleOutput.WriteLine("\nPreview before sync avoids unexpected data changes.");
            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrate different conflict resolution modes.
        /// </summary>
        public static Task RunConflictResolutionExample()
        {
            ExampleOutput.WriteLine("=== Conflict Resolution Example ===\n");

            // 1. LocalWins - local version always takes precedence
            var localWins = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.LocalWins
            };
            ExampleOutput.WriteLine($"Policy: {localWins.ConflictPolicy} - local data is kept on conflict");

            // 2. RemoteWins - remote version always takes precedence
            var remoteWins = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.RemoteWins
            };
            ExampleOutput.WriteLine($"Policy: {remoteWins.ConflictPolicy} - remote data overwrites local on conflict");

            // 3. NewestWins - the version with the latest timestamp wins
            var newestWins = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.NewestWins
            };
            ExampleOutput.WriteLine($"Policy: {newestWins.ConflictPolicy} - newest timestamp wins on conflict");

            // 4. Custom - user-defined conflict resolution logic
            var custom = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.Custom,
                CustomConflictResolver = conflict =>
                {
                    ExampleOutput.WriteLine($"  Resolving conflict for item {conflict.Guid}: {conflict.Reason}");
                    // Return UseLocal, UseRemote, Merge, or Skip
                    return ConflictResolution.UseRemote;
                },
                OnConflict = conflict =>
                {
                    ExampleOutput.WriteLine($"  Conflict detected: {conflict.Guid} - {conflict.Reason}");
                }
            };
            ExampleOutput.WriteLine($"Policy: {custom.ConflictPolicy} - custom resolver decides per item");

            ExampleOutput.WriteLine("\nConflictResolution enum values:");
            ExampleOutput.WriteLine("  UseLocal  - keep the local version");
            ExampleOutput.WriteLine("  UseRemote - keep the remote version");
            ExampleOutput.WriteLine("  Merge     - merge both versions (if supported)");
            ExampleOutput.WriteLine("  Skip      - skip this item entirely");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrate multi-tenant sync using scoped sync operations.
        /// </summary>
        public static Task RunMultiTenantSyncExample()
        {
            ExampleOutput.WriteLine("=== Multi-Tenant Sync Example ===\n");

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();

            // Each tenant gets its own scope to isolate sync state
            var tenantAOptions = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = $"Documents_Tenant_{tenantAId}",
                ConflictPolicy = ConflictResolutionPolicy.NewestWins,
                BatchSize = 100
            };

            var tenantBOptions = new SyncOptions
            {
                Direction = SyncDirection.Download,
                Scope = $"Documents_Tenant_{tenantBId}",
                ConflictPolicy = ConflictResolutionPolicy.RemoteWins,
                BatchSize = 50
            };

            ExampleOutput.WriteLine($"Tenant A scope: {tenantAOptions.Scope}");
            ExampleOutput.WriteLine($"  Direction: {tenantAOptions.Direction}, Policy: {tenantAOptions.ConflictPolicy}");
            ExampleOutput.WriteLine($"Tenant B scope: {tenantBOptions.Scope}");
            ExampleOutput.WriteLine($"  Direction: {tenantBOptions.Direction}, Policy: {tenantBOptions.ConflictPolicy}");

            // Use SyncFilterOptions<T> to restrict which items each tenant can sync:
            // var filterA = new SyncFilterOptions<ExampleDocument>
            // {
            //     LocalFetchPredicate = doc => doc.TenantGuid == tenantAId,
            //     RemoteFetchPredicate = doc => doc.TenantGuid == tenantAId,
            //     CanSaveToLocal = doc => doc.TenantGuid == tenantAId,
            //     CanSaveToRemote = doc => doc.TenantGuid == tenantAId,
            //     OnSaveFilterBlock = SaveFilterBlockAction.Skip
            // };
            // provider.Sync(tenantAOptions, filterA);

            ExampleOutput.WriteLine("\nSyncFilterOptions ensures tenant data isolation during sync.");
            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrate SyncQueue for managing concurrent sync operations.
        /// </summary>
        public static Task RunSyncQueueExample()
        {
            ExampleOutput.WriteLine("=== Sync Queue Example ===\n");

            // SyncQueue ensures only one sync runs per scope at a time
            var syncQueue = new SyncQueue(maxConcurrentSyncs: 2);

            ExampleOutput.WriteLine($"Max concurrent syncs: {syncQueue.MaxConcurrentSyncs}");
            ExampleOutput.WriteLine($"Queue length for 'Documents': {syncQueue.GetQueueLength("Documents")}");

            // Enqueue sync operations (they will run sequentially per scope):
            // var result = await syncQueue.EnqueueAsync("Documents", async () =>
            // {
            //     return provider.Sync(options);
            // });

            // Check all queue lengths
            var queueLengths = syncQueue.GetAllQueueLengths();
            ExampleOutput.WriteLine($"Active queues: {queueLengths.Count}");

            // Clear all queued operations
            syncQueue.Clear();
            ExampleOutput.WriteLine("All queues cleared.");

            ExampleOutput.WriteLine("\nSyncQueue prevents concurrent syncs on the same scope,");
            ExampleOutput.WriteLine("avoiding data corruption from overlapping operations.");
            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrate custom conflict resolver with detailed item inspection.
        /// </summary>
        public static Task RunCustomConflictResolverExample()
        {
            ExampleOutput.WriteLine("=== Custom Conflict Resolver Example ===\n");

            var options = new SyncOptions
            {
                Direction = SyncDirection.Bidirectional,
                Scope = "Documents",
                ConflictPolicy = ConflictResolutionPolicy.Custom,
                CustomConflictResolver = conflict =>
                {
                    // ConflictInfo provides both versions for comparison
                    ExampleOutput.WriteLine($"  Item: {conflict.Guid}");
                    ExampleOutput.WriteLine($"  Reason: {conflict.Reason}");
                    ExampleOutput.WriteLine($"  Local item: {conflict.LocalItem}");
                    ExampleOutput.WriteLine($"  Remote item: {conflict.RemoteItem}");

                    // Custom logic: cast to your model type and compare
                    // var local = conflict.LocalItem as ExampleDocument;
                    // var remote = conflict.RemoteItem as ExampleDocument;
                    // if (local?.Content?.Length > remote?.Content?.Length)
                    //     return ConflictResolution.UseLocal;

                    return ConflictResolution.UseRemote;
                },
                OnBatchStarting = batchNumber =>
                {
                    ExampleOutput.WriteLine($"  Starting batch {batchNumber}...");
                },
                OnBatchCompleted = batchResult =>
                {
                    ExampleOutput.WriteLine($"  Batch {batchResult.BatchNumber}: processed {batchResult.Processed}, errors: {batchResult.Errors.Count}");
                }
            };

            ExampleOutput.WriteLine($"Conflict policy: {options.ConflictPolicy}");
            ExampleOutput.WriteLine("Custom resolver inspects LocalItem and RemoteItem from ConflictInfo");
            ExampleOutput.WriteLine("and returns a ConflictResolution decision per item.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrate SyncFilterOptions for tenant-aware filtering during sync.
        /// </summary>
        public static Task RunTenantAwareSyncExample()
        {
            ExampleOutput.WriteLine("=== Tenant-Aware Sync Example ===\n");

            var tenantGuid = Guid.NewGuid();

            // SyncFilterOptions<T> provides fine-grained control over which items sync
            ExampleOutput.WriteLine("SyncFilterOptions<T> properties:");
            ExampleOutput.WriteLine("  LocalFetchPredicate  - Expression<Func<T, bool>> filtering local items to sync");
            ExampleOutput.WriteLine("  RemoteFetchPredicate - Expression<Func<T, bool>> filtering remote items to sync");
            ExampleOutput.WriteLine("  CanSaveToLocal       - Func<T, bool> gate before writing to local store");
            ExampleOutput.WriteLine("  CanSaveToRemote      - Func<T, bool> gate before writing to remote store");
            ExampleOutput.WriteLine("  OnSaveFilterBlock    - Action when a save filter blocks an item:");
            ExampleOutput.WriteLine("    Skip          - silently skip");
            ExampleOutput.WriteLine("    LogAsError    - log as error, continue");
            ExampleOutput.WriteLine("    ThrowException - fail the sync");
            ExampleOutput.WriteLine("    MarkConflict  - flag for manual review");

            ExampleOutput.WriteLine($"\nExample: filtering sync for tenant {tenantGuid}");
            ExampleOutput.WriteLine("  LocalFetchPredicate = doc => doc.TenantGuid == tenantGuid");
            ExampleOutput.WriteLine("  RemoteFetchPredicate = doc => doc.TenantGuid == tenantGuid");
            ExampleOutput.WriteLine("  CanSaveToLocal = doc => doc.TenantGuid == tenantGuid");
            ExampleOutput.WriteLine("  OnSaveFilterBlock = SaveFilterBlockAction.MarkConflict");

            // Using CancellationToken for timeout:
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var options = new SyncOptions
            {
                Direction = SyncDirection.Download,
                Scope = $"TenantDocs_{tenantGuid}",
                CancellationToken = cts.Token,
                MaxItems = 1000
            };
            ExampleOutput.WriteLine($"\nSync scope: {options.Scope}");
            ExampleOutput.WriteLine($"Max items: {options.MaxItems}");
            ExampleOutput.WriteLine($"Cancellation: after 5 minutes");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }
    }
}
