using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Core.Data;

/// <summary>Coarse offline/sync state of a <see cref="MirrorDataSource{T}"/> — bound by a sync indicator.</summary>
public enum SyncStatus
{
    /// <summary>Last read reached the remote source; local mirror is current.</summary>
    Synced,
    /// <summary>A read is in flight against the remote source.</summary>
    Syncing,
    /// <summary>The remote source was unreachable; reads are being served from the local mirror.</summary>
    Offline,
}

/// <summary>
/// A network-first read-through mirror over the <see cref="ICrudDataSource{T}"/> port — the XAML
/// analogue of Birko.Web's <c>MirrorStore</c> / <c>readThrough</c>. Reads try the <c>remote</c>
/// source first; on success the local <c>mirror</c> is refreshed (and stale-not-found entries
/// evicted), and on failure (offline) the reads fall back to the mirror. Writes pass through to the
/// remote and, on success, to the mirror. Platform-neutral (Avalonia-free); the concrete offline
/// backing is any <see cref="ICrudDataSource{T}"/> the consumer supplies (e.g. a local file/SQLite store).
///
/// <para>The IndexedDB implementation of the web version is browser-specific; only the port/behaviour
/// ports. A write-queue/outbox (for offline writes) is out of scope here — this is the read path.</para>
/// </summary>
public partial class MirrorDataSource<T> : ObservableObject, ICrudDataSource<T>
    where T : class
{
    private readonly ICrudDataSource<T> _remote;
    private readonly ICrudDataSource<T> _mirror;
    private readonly Func<T, Guid> _idOf;

    /// <param name="remote">The authoritative source (server).</param>
    /// <param name="mirror">The local offline copy (e.g. a file/SQLite-backed <see cref="ICrudDataSource{T}"/>).</param>
    /// <param name="idOf">Extracts an entity's stable id (used to upsert/evict in the mirror).</param>
    public MirrorDataSource(ICrudDataSource<T> remote, ICrudDataSource<T> mirror, Func<T, Guid> idOf)
    {
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _idOf = idOf ?? throw new ArgumentNullException(nameof(idOf));
    }

    /// <summary>Current offline/sync state — bind a sync indicator to this.</summary>
    [ObservableProperty]
    private SyncStatus _status = SyncStatus.Synced;

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        Status = SyncStatus.Syncing;
        try
        {
            var remoteItems = await _remote.GetAllAsync(ct).ConfigureAwait(false);
            await ReplaceMirrorAsync(remoteItems, ct).ConfigureAwait(false);
            Status = SyncStatus.Synced;
            return remoteItems;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Status = SyncStatus.Offline;                       // remote unreachable → serve the mirror
            return await _mirror.GetAllAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<T?> GetAsync(Guid id, CancellationToken ct = default)
    {
        Status = SyncStatus.Syncing;
        try
        {
            var item = await _remote.GetAsync(id, ct).ConfigureAwait(false);
            if (item is null)
            {
                await EvictAsync(id, ct).ConfigureAwait(false); // gone on the server → drop from mirror
            }
            else
            {
                await _mirror.SaveAsync(item, ct).ConfigureAwait(false);
            }
            Status = SyncStatus.Synced;
            return item;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Status = SyncStatus.Offline;
            return await _mirror.GetAsync(id, ct).ConfigureAwait(false);
        }
    }

    public async Task<Guid> SaveAsync(T item, CancellationToken ct = default)
    {
        var id = await _remote.SaveAsync(item, ct).ConfigureAwait(false);
        try { await _mirror.SaveAsync(item, ct).ConfigureAwait(false); } catch { /* mirror is best-effort */ }
        return id;
    }

    public async Task DeleteAsync(T item, CancellationToken ct = default)
    {
        await _remote.DeleteAsync(item, ct).ConfigureAwait(false);
        try { await _mirror.DeleteAsync(item, ct).ConfigureAwait(false); } catch { /* mirror is best-effort */ }
    }

    public T NewInstance() => _remote.NewInstance();

    private async Task ReplaceMirrorAsync(IReadOnlyList<T> remoteItems, CancellationToken ct)
    {
        var keep = new HashSet<Guid>();
        foreach (var item in remoteItems)
        {
            keep.Add(_idOf(item));
            await _mirror.SaveAsync(item, ct).ConfigureAwait(false);
        }
        // Evict mirror entries no longer present on the remote.
        var mirrored = await _mirror.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var stale in mirrored.Where(m => !keep.Contains(_idOf(m))).ToList())
        {
            await _mirror.DeleteAsync(stale, ct).ConfigureAwait(false);
        }
    }

    private async Task EvictAsync(Guid id, CancellationToken ct)
    {
        var existing = await _mirror.GetAsync(id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await _mirror.DeleteAsync(existing, ct).ConfigureAwait(false);
        }
    }
}
