using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// A <see cref="IJobLockProvider"/> backed by an exclusive OS file handle. **Session-scoped**, not
    /// lease-based: the kernel releases the lock when the holding process dies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the stronger of the two guarantees, and that is the opposite of what was expected.</b>
    /// TASK-232's first sketch listed the file-backed job queues as "probably no locking" on the grounds
    /// that file locking is unportable. Measured instead (TASK-236), on Windows and on Linux/.NET 9: a
    /// second process is refused while the first holds the handle, and after the holder is killed with no
    /// chance to release — <c>taskkill /F</c>, <c>kill -9</c> — the next caller acquires immediately. That
    /// is what a session lock means, and no document store in this family can offer it: CosmosDB,
    /// ElasticSearch, MongoDB and RavenDB can express only a time-bounded lease, because none has a
    /// server-side notion of "release this when that client disappears".
    /// </para>
    /// <para>
    /// <b>Scope: processes that share a filesystem.</b> That is the deployment the JSON and XML job queues
    /// already assume — they coordinate through a shared directory. It is *not* a distributed lock: two
    /// machines with their own disks will both acquire. On Unix the exclusion is advisory (it binds
    /// processes that open the file the same way, which is every .NET caller of this class) rather than
    /// mandatory against arbitrary processes. A network share adds its own failure modes — SMB and
    /// especially NFS have historically been unreliable for locking — and is untested here.
    /// </para>
    /// <para>
    /// ⚠ <b>A lock does not make the file-backed queues cross-process safe.</b> `JsonJobQueue` serializes
    /// its read-claim-update with an in-process semaphore and says so: the file store has no
    /// compare-and-swap, so two processes can still claim the same job. What this provider buys is
    /// coordination *around* the queue — leader election for <see cref="RecurringJobScheduler"/>, where
    /// exactly one process is entitled to enqueue (TASK-237).
    /// </para>
    /// </remarks>
    public sealed class FileJobLockProvider : IJobLockProvider
    {
        private readonly string _directory;
        private FileStream? _handle;
        private string? _path;
        private bool _disposed;

        /// <summary>Poll interval floor while waiting for a holder to release.</summary>
        private static readonly TimeSpan MinBackoff = TimeSpan.FromMilliseconds(50);

        /// <summary>Poll interval ceiling.</summary>
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Creates a provider that keeps its lock files in <paramref name="directory"/>. All processes
        /// meant to exclude one another must point at the same directory.
        /// </summary>
        public FileJobLockProvider(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A lock directory is required.", nameof(directory));
            }

            _directory = directory;

            // Created up front so that a DirectoryNotFoundException later means something genuinely went
            // wrong, rather than "nobody has written here yet" — which is what lets TryAcquireAsync tell
            // contention from an environment fault below.
            Directory.CreateDirectory(_directory);
        }

        /// <summary>Whether this provider currently believes it holds a lock.</summary>
        public bool IsLocked => _handle != null;

        /// <summary>
        /// Always false: the lock lives on an open handle, so the operating system releases it when this
        /// process dies. No lease, no heartbeat, nothing to expire mid-work.
        /// </summary>
        public bool IsLeaseBased => false;

        /// <summary>
        /// Attempts to acquire the named lock, polling until <paramref name="acquireTimeout"/> elapses.
        /// </summary>
        /// <remarks>
        /// <paramref name="leaseDuration"/> must be <c>null</c>. A file handle has no expiry — it is
        /// released explicitly, or by the kernel when the process ends. Accepting a duration and ignoring
        /// it would promise a bound this provider cannot enforce, which is the lie TASK-232 removed from
        /// the interface, so it throws instead. This mirrors <c>SqlJobLockProvider</c> exactly, because
        /// the two are the same kind of lock.
        /// </remarks>
        public async Task<bool> TryAcquireAsync(
            string lockName,
            TimeSpan acquireTimeout,
            TimeSpan? leaseDuration = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (leaseDuration.HasValue)
            {
                throw new ArgumentException(
                    "A file lock is session-scoped and cannot expire, so a lease duration cannot be " +
                    "honoured. Pass null for a session lock, or use a provider whose IsLeaseBased is true.",
                    nameof(leaseDuration));
            }

            if (IsLocked)
            {
                return true;
            }

            var path = PathFor(lockName);
            var deadline = DateTime.UtcNow.Add(acquireTimeout);
            var backoff = MinBackoff;

            while (true)
            {
                try
                {
                    // FileShare.None is the whole mechanism: the handle stays open for as long as the lock
                    // is held, and the OS refuses every other opener until it closes.
                    _handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    _path = path;
                    return true;
                }
                catch (DirectoryNotFoundException)
                {
                    // Not contention — the directory this provider created has gone. Surface it rather
                    // than reporting a lost race forever, which is how a misconfiguration turns into a
                    // process that silently never leads.
                    throw;
                }
                catch (IOException)
                {
                    // Contention. The HResult differs per platform (0x80070020 sharing violation on
                    // Windows, 0x0000000B EAGAIN on Linux), so the discrimination is by exception TYPE,
                    // which is portable: a bad path throws DirectoryNotFoundException and a permissions
                    // problem throws UnauthorizedAccessException, neither of which is caught here.
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                if (backoff < MaxBackoff)
                {
                    backoff += MinBackoff;
                }
            }
        }

        /// <summary>Releases the lock. Releasing one that is not held is not an error.</summary>
        public Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCore();
            return Task.CompletedTask;
        }

        private void ReleaseCore()
        {
            var handle = _handle;
            var path = _path;
            _handle = null;
            _path = null;

            if (handle == null)
            {
                return;
            }

            handle.Dispose();

            // Best effort. The lock is the HANDLE, not the file, so a leftover file locks nothing and a
            // failure to delete it must not look like a failure to release. Deleting is only tidiness —
            // and it can legitimately fail if another process has already opened the file behind us.
            try
            {
                if (path != null)
                {
                    File.Delete(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Maps a lock name to a file inside the provider's directory.
        /// </summary>
        /// <remarks>
        /// The name reaches a path, so it is sanitised rather than trusted: every character that is not
        /// plainly safe becomes '_'. Without that, a caller-supplied name could walk out of the directory
        /// with <c>..</c> or a separator and take a lock on — or create a file at — somewhere else
        /// entirely. Same reasoning as resolving an identifier before it reaches SQL text: what survives is
        /// built here, not accepted from the caller.
        /// </remarks>
        private string PathFor(string lockName)
        {
            if (string.IsNullOrWhiteSpace(lockName))
            {
                throw new ArgumentException("A lock needs a name.", nameof(lockName));
            }

            var safe = new char[lockName.Length];
            for (var i = 0; i < lockName.Length; i++)
            {
                var c = lockName[i];
                safe[i] = (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') ? c : '_';
            }

            var name = new string(safe).Trim('.');
            if (name.Length == 0)
            {
                throw new ArgumentException(
                    "A lock name must contain at least one usable character.", nameof(lockName));
            }

            return Path.Combine(_directory, name + ".lock");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseCore();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
