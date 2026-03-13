using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.BackgroundJobs.Redis
{
    /// <summary>
    /// Redis-based persistent job queue implementing IJobQueue.
    /// Uses Redis hashes for job storage and sorted sets for queue ordering.
    /// Jobs survive process restarts and support distributed processing.
    /// </summary>
    public class RedisJobQueue : IJobQueue, IDisposable
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly RedisSettings _settings;
        private readonly RetryPolicy _retryPolicy;
        private readonly bool _ownsConnection;

        private string KeyPrefix => _settings.KeyPrefix ?? "birko:jobs";

        // Lua script for atomic dequeue: finds best candidate in sorted set, removes it, and returns the job ID.
        // This prevents two workers from grabbing the same job.
        private const string DequeueScript = @"
            local key = KEYS[1]
            local now = tonumber(ARGV[1])
            local members = redis.call('ZRANGEBYSCORE', key, '-inf', now, 'LIMIT', 0, 1)
            if #members == 0 then
                return nil
            end
            redis.call('ZREM', key, members[1])
            return members[1]
        ";

        /// <summary>
        /// Creates a new Redis job queue with connection settings.
        /// </summary>
        /// <param name="settings">Redis connection settings.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public RedisJobQueue(RedisSettings settings, RetryPolicy? retryPolicy = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionManager = new RedisConnectionManager(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _ownsConnection = true;
        }

        /// <summary>
        /// Creates a new Redis job queue from an existing connection manager.
        /// </summary>
        /// <param name="connectionManager">A pre-configured connection manager.</param>
        /// <param name="settings">Redis settings (for key prefix configuration).</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public RedisJobQueue(RedisConnectionManager connectionManager, RedisSettings settings, RetryPolicy? retryPolicy = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _ownsConnection = false;
        }

        /// <summary>
        /// Gets the underlying connection manager for advanced scenarios.
        /// </summary>
        public RedisConnectionManager ConnectionManager => _connectionManager;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(descriptor.Id);

            var fields = SerializeDescriptor(descriptor);
            await db.HashSetAsync(jobKey, fields).ConfigureAwait(false);

            // Add to the appropriate sorted set for dequeue ordering
            // Score: negative priority * 1e13 + enqueued ticks (lower score = higher priority, older first)
            var score = GetQueueScore(descriptor.Priority, descriptor.ScheduledAt ?? descriptor.EnqueuedAt);
            var queueKey = GetQueueKey(descriptor.QueueName);
            await db.SortedSetAddAsync(queueKey, descriptor.Id.ToString(), score).ConfigureAwait(false);

            // Track in status set
            await db.SetAddAsync(GetStatusKey(descriptor.Status), descriptor.Id.ToString()).ConfigureAwait(false);

            return descriptor.Id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var queueKey = GetQueueKey(queueName);
            var now = DateTime.UtcNow.Ticks;

            // Atomic dequeue using Lua script
            var result = await db.ScriptEvaluateAsync(
                DequeueScript,
                new RedisKey[] { queueKey },
                new RedisValue[] { now }
            ).ConfigureAwait(false);

            if (result.IsNull)
            {
                return null;
            }

            var jobIdStr = result.ToString();
            if (!Guid.TryParse(jobIdStr, out var jobId))
            {
                return null;
            }

            var jobKey = GetJobKey(jobId);
            var fields = await db.HashGetAllAsync(jobKey).ConfigureAwait(false);
            if (fields.Length == 0)
            {
                return null;
            }

            var descriptor = DeserializeDescriptor(fields);

            // Remove from old status set
            await db.SetRemoveAsync(GetStatusKey(descriptor.Status), jobIdStr).ConfigureAwait(false);

            // Mark as processing
            descriptor.Status = JobStatus.Processing;
            descriptor.AttemptCount++;
            descriptor.LastAttemptAt = DateTime.UtcNow;

            // Update in Redis
            await db.HashSetAsync(jobKey, SerializeDescriptor(descriptor)).ConfigureAwait(false);
            await db.SetAddAsync(GetStatusKey(JobStatus.Processing), jobIdStr).ConfigureAwait(false);

            return descriptor;
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(jobId);
            var jobIdStr = jobId.ToString();

            if (!await db.KeyExistsAsync(jobKey).ConfigureAwait(false))
            {
                return;
            }

            var oldStatus = await GetJobStatusAsync(db, jobKey).ConfigureAwait(false);
            await db.SetRemoveAsync(GetStatusKey(oldStatus), jobIdStr).ConfigureAwait(false);

            await db.HashSetAsync(jobKey, new[]
            {
                new HashEntry("Status", (int)JobStatus.Completed),
                new HashEntry("CompletedAt", DateTime.UtcNow.Ticks)
            }).ConfigureAwait(false);

            await db.SetAddAsync(GetStatusKey(JobStatus.Completed), jobIdStr).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(jobId);
            var jobIdStr = jobId.ToString();

            if (!await db.KeyExistsAsync(jobKey).ConfigureAwait(false))
            {
                return;
            }

            var fields = await db.HashGetAllAsync(jobKey).ConfigureAwait(false);
            var descriptor = DeserializeDescriptor(fields);

            // Remove from old status set
            await db.SetRemoveAsync(GetStatusKey(descriptor.Status), jobIdStr).ConfigureAwait(false);

            descriptor.LastError = error;

            if (descriptor.AttemptCount < descriptor.MaxRetries)
            {
                var delay = _retryPolicy.GetDelay(descriptor.AttemptCount);
                descriptor.Status = JobStatus.Scheduled;
                descriptor.ScheduledAt = DateTime.UtcNow.Add(delay);

                // Re-add to queue sorted set with scheduled time as score
                var queueKey = GetQueueKey(descriptor.QueueName);
                var score = GetQueueScore(descriptor.Priority, descriptor.ScheduledAt.Value);
                await db.SortedSetAddAsync(queueKey, jobIdStr, score).ConfigureAwait(false);
            }
            else
            {
                descriptor.Status = JobStatus.Dead;
                descriptor.CompletedAt = DateTime.UtcNow;
            }

            await db.HashSetAsync(jobKey, SerializeDescriptor(descriptor)).ConfigureAwait(false);
            await db.SetAddAsync(GetStatusKey(descriptor.Status), jobIdStr).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(jobId);
            var jobIdStr = jobId.ToString();

            if (!await db.KeyExistsAsync(jobKey).ConfigureAwait(false))
            {
                return false;
            }

            var status = await GetJobStatusAsync(db, jobKey).ConfigureAwait(false);

            if (status != JobStatus.Pending && status != JobStatus.Scheduled)
            {
                return false;
            }

            // Remove from old status set and queue
            await db.SetRemoveAsync(GetStatusKey(status), jobIdStr).ConfigureAwait(false);

            // Remove from all queue sorted sets (we don't know the exact queue name from the hash alone)
            var queueName = (string?)await db.HashGetAsync(jobKey, "QueueName").ConfigureAwait(false);
            var queueKey = GetQueueKey(queueName);
            await db.SortedSetRemoveAsync(queueKey, jobIdStr).ConfigureAwait(false);

            await db.HashSetAsync(jobKey, new[]
            {
                new HashEntry("Status", (int)JobStatus.Cancelled),
                new HashEntry("CompletedAt", DateTime.UtcNow.Ticks)
            }).ConfigureAwait(false);

            await db.SetAddAsync(GetStatusKey(JobStatus.Cancelled), jobIdStr).ConfigureAwait(false);

            return true;
        }

        public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(jobId);

            var fields = await db.HashGetAllAsync(jobKey).ConfigureAwait(false);
            if (fields.Length == 0)
            {
                return null;
            }

            return DeserializeDescriptor(fields);
        }

        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var statusKey = GetStatusKey(status);

            var members = await db.SetMembersAsync(statusKey).ConfigureAwait(false);
            var result = new List<JobDescriptor>();

            foreach (var member in members.Take(limit))
            {
                if (!Guid.TryParse(member.ToString(), out var jobId))
                {
                    continue;
                }

                var jobKey = GetJobKey(jobId);
                var fields = await db.HashGetAllAsync(jobKey).ConfigureAwait(false);
                if (fields.Length > 0)
                {
                    result.Add(DeserializeDescriptor(fields));
                }
            }

            return result.OrderByDescending(j => j.EnqueuedAt).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var db = _connectionManager.GetDatabase();
            var cutoff = DateTime.UtcNow.Subtract(olderThan);
            var count = 0;

            foreach (var status in new[] { JobStatus.Completed, JobStatus.Dead, JobStatus.Cancelled })
            {
                var statusKey = GetStatusKey(status);
                var members = await db.SetMembersAsync(statusKey).ConfigureAwait(false);

                foreach (var member in members)
                {
                    if (!Guid.TryParse(member.ToString(), out var jobId))
                    {
                        continue;
                    }

                    var jobKey = GetJobKey(jobId);
                    var completedAtValue = await db.HashGetAsync(jobKey, "CompletedAt").ConfigureAwait(false);

                    if (completedAtValue.HasValue && long.TryParse(completedAtValue.ToString(), out var ticks))
                    {
                        var completedAt = new DateTime(ticks, DateTimeKind.Utc);
                        if (completedAt < cutoff)
                        {
                            await db.KeyDeleteAsync(jobKey).ConfigureAwait(false);
                            await db.SetRemoveAsync(statusKey, member).ConfigureAwait(false);
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        public void Dispose()
        {
            if (_ownsConnection)
            {
                _connectionManager.Dispose();
            }
        }

        #region Key Helpers

        private string GetJobKey(Guid jobId) => $"{KeyPrefix}:job:{jobId}";

        private string GetQueueKey(string? queueName) =>
            queueName != null
                ? $"{KeyPrefix}:queue:{queueName}"
                : $"{KeyPrefix}:queue:default";

        private string GetStatusKey(JobStatus status) =>
            $"{KeyPrefix}:status:{(int)status}";

        #endregion

        #region Scoring

        /// <summary>
        /// Calculates queue score for sorted set ordering.
        /// Lower score = dequeued first. Higher priority jobs get lower scores.
        /// Score = -(priority * 1e13) + scheduledAt.Ticks / 1e4
        /// This ensures priority dominates, with FIFO within same priority.
        /// </summary>
        private static double GetQueueScore(int priority, DateTime scheduledAt)
        {
            return -(priority * 1e13) + (scheduledAt.Ticks / 1e4);
        }

        #endregion

        #region Serialization

        private static HashEntry[] SerializeDescriptor(JobDescriptor descriptor)
        {
            var entries = new List<HashEntry>
            {
                new("Id", descriptor.Id.ToString()),
                new("JobType", descriptor.JobType),
                new("Status", (int)descriptor.Status),
                new("Priority", descriptor.Priority),
                new("MaxRetries", descriptor.MaxRetries),
                new("AttemptCount", descriptor.AttemptCount),
                new("EnqueuedAt", descriptor.EnqueuedAt.Ticks)
            };

            if (descriptor.InputType != null)
                entries.Add(new HashEntry("InputType", descriptor.InputType));
            if (descriptor.SerializedInput != null)
                entries.Add(new HashEntry("SerializedInput", descriptor.SerializedInput));
            if (descriptor.QueueName != null)
                entries.Add(new HashEntry("QueueName", descriptor.QueueName));
            if (descriptor.ScheduledAt.HasValue)
                entries.Add(new HashEntry("ScheduledAt", descriptor.ScheduledAt.Value.Ticks));
            if (descriptor.LastAttemptAt.HasValue)
                entries.Add(new HashEntry("LastAttemptAt", descriptor.LastAttemptAt.Value.Ticks));
            if (descriptor.CompletedAt.HasValue)
                entries.Add(new HashEntry("CompletedAt", descriptor.CompletedAt.Value.Ticks));
            if (descriptor.LastError != null)
                entries.Add(new HashEntry("LastError", descriptor.LastError));
            if (descriptor.Metadata.Count > 0)
                entries.Add(new HashEntry("MetadataJson", JsonSerializer.Serialize(descriptor.Metadata)));

            return entries.ToArray();
        }

        private static JobDescriptor DeserializeDescriptor(HashEntry[] fields)
        {
            var dict = fields.ToDictionary(f => f.Name.ToString(), f => f.Value);

            var descriptor = new JobDescriptor
            {
                Id = Guid.Parse(dict["Id"].ToString()),
                JobType = dict["JobType"]!,
                Status = (JobStatus)(int)dict["Status"],
                Priority = (int)dict["Priority"],
                MaxRetries = (int)dict["MaxRetries"],
                AttemptCount = (int)dict["AttemptCount"],
                EnqueuedAt = new DateTime((long)dict["EnqueuedAt"], DateTimeKind.Utc)
            };

            if (dict.TryGetValue("InputType", out var inputType) && inputType.HasValue)
                descriptor.InputType = inputType!;
            if (dict.TryGetValue("SerializedInput", out var serializedInput) && serializedInput.HasValue)
                descriptor.SerializedInput = serializedInput!;
            if (dict.TryGetValue("QueueName", out var queueName) && queueName.HasValue)
                descriptor.QueueName = queueName!;
            if (dict.TryGetValue("ScheduledAt", out var scheduledAt) && scheduledAt.HasValue)
                descriptor.ScheduledAt = new DateTime((long)scheduledAt, DateTimeKind.Utc);
            if (dict.TryGetValue("LastAttemptAt", out var lastAttemptAt) && lastAttemptAt.HasValue)
                descriptor.LastAttemptAt = new DateTime((long)lastAttemptAt, DateTimeKind.Utc);
            if (dict.TryGetValue("CompletedAt", out var completedAt) && completedAt.HasValue)
                descriptor.CompletedAt = new DateTime((long)completedAt, DateTimeKind.Utc);
            if (dict.TryGetValue("LastError", out var lastError) && lastError.HasValue)
                descriptor.LastError = lastError!;
            if (dict.TryGetValue("MetadataJson", out var metadataJson) && metadataJson.HasValue)
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson.ToString());
                if (metadata != null)
                {
                    descriptor.Metadata = metadata;
                }
            }

            return descriptor;
        }

        private static async Task<JobStatus> GetJobStatusAsync(IDatabase db, string jobKey)
        {
            var statusValue = await db.HashGetAsync(jobKey, "Status").ConfigureAwait(false);
            return statusValue.HasValue ? (JobStatus)(int)statusValue : JobStatus.Pending;
        }

        #endregion
    }
}
