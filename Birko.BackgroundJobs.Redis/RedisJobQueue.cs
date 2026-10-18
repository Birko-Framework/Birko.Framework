using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Redis;
using Birko.Serialization;
using Birko.Serialization.Json;
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
        private readonly ISerializer _serializer;
        private readonly bool _ownsConnection;

        /// <summary>How long a job may stay Processing before it is presumed abandoned (TASK-664; see <see cref="JobReclaim"/>).</summary>
        private readonly TimeSpan _claimTimeout;

        private string KeyPrefix => _settings.KeyPrefix ?? "birko:jobs";

        /// <summary>
        /// How many due members of the shared <c>default</c> key a named dequeue inspects to find an unnamed job
        /// (see the dequeue script). Bounds the script's work per claim.
        /// </summary>
        private const int UnnamedScanLimit = 100;

        // TASK-663: every state change is ONE Lua script, so no transition is ever half-applied and every guard is checked
        // by the same atomic step that writes. Each queue has two sorted sets:
        //   {prefix}:queue:{name} — DELAYED: score = the time the job becomes due (ticks / 1e4, the scale it always had,
        //                           so jobs stored before TASK-663 are read as delayed and promoted when due);
        //   {prefix}:ready:{name} — READY:   score = rank (priority, then EnqueuedAt), computed by RankLua below.
        // Eligibility and order are decoupled: the dequeue promotes due members to READY and claims the best-ranked one,
        // so priority dominates among due jobs as on every other backend (SH-M015; it used to span < 1 ms of score).

        // Rank among ready jobs: priority clamped to [-999, 999] dominates (higher first), then EnqueuedAt in milliseconds
        // since 2020 (older first). (999 - p) * 1e12 + ms stays below 2^53, so every rank is an exact integer.
        private const string RankLua = @"
            local function rank(jobKey)
                local f = redis.call('HMGET', jobKey, 'Priority', 'EnqueuedAt')
                local p = tonumber(f[1] or '0') or 0
                if p > 999 then p = 999 elseif p < -999 then p = -999 end
                local ms = math.floor((tonumber(f[2] or '0') or 0) / 10000) - 63713433600000
                return (999 - p) * 1000000000000 + ms
            end
            local function runnable(st)
                return st == ARGV[2] or st == ARGV[3]
            end
            local function promote(delayedKey, readyKey, now, prefix, limit)
                local due = redis.call('ZRANGEBYSCORE', delayedKey, '-inf', now, 'LIMIT', 0, limit)
                for i = 1, #due do
                    redis.call('ZREM', delayedKey, due[i])
                    local jobKey = prefix .. ':job:' .. due[i]
                    if runnable(redis.call('HGET', jobKey, 'Status')) then
                        redis.call('ZADD', readyKey, rank(jobKey), due[i])
                    end
                end
            end
        ";

        // Dequeue (CR-M025, FIELD-028, TASK-663, TASK-664). KEYS: [1] named delayed, [2] named ready, [3] unnamed delayed,
        // [4] unnamed ready, [5] named processing, [6] unnamed processing. ARGV: [1] prefix, [2] Pending int, [3] Scheduled
        // int, [4] Processing int, [5] now threshold, [6] LastAttemptAt ticks, [7] "1" when the unnamed keys are different
        // keys that must also be read, [8] how many ready unnamed members to inspect, [9] how many due members to promote
        // per key, [10] stale threshold, [11] the policy's MaxRetries, [12] the abandoned-error template, [13] Dead int.
        // TASK-664: a member of a processing set claimed before the stale threshold was abandoned by its worker. It goes
        // back to READY as Scheduled, or is buried as Dead when its attempt budget is spent (the job's MaxRetries, or the
        // policy's when the job sets none). A member whose job is no longer Processing is dropped.
        // A member whose hash is gone or no longer runnable (cancelled, or enqueued as Completed / Dead / Processing) is
        // dropped from READY, never claimed (SH-M014). An unnamed job shares the `default` keys with jobs explicitly named
        // "default", which must stay on their queue, so only members whose hash carries no QueueName qualify there.
        private const string DequeueScript = RankLua + @"
            local prefix = ARGV[1]
            local now = tonumber(ARGV[5])
            local scan = tonumber(ARGV[8])
            local promoteLimit = tonumber(ARGV[9])
            local readUnnamed = ARGV[7] == '1'
            local function reclaim(processingKey, readyKey)
                local stale = redis.call('ZRANGEBYSCORE', processingKey, '-inf', '(' .. ARGV[10], 'LIMIT', 0, promoteLimit)
                for i = 1, #stale do
                    local id = stale[i]
                    local jobKey = prefix .. ':job:' .. id
                    redis.call('ZREM', processingKey, id)
                    if redis.call('HGET', jobKey, 'Status') == ARGV[4] then
                        local attempts = tonumber(redis.call('HGET', jobKey, 'AttemptCount') or '0')
                        local max = redis.call('HGET', jobKey, 'MaxRetries')
                        if not max or max == '' then max = ARGV[11] end
                        redis.call('SREM', prefix .. ':status:' .. ARGV[4], id)
                        if attempts >= tonumber(max) then
                            local message = string.gsub(ARGV[12], '{attempts}', tostring(attempts))
                            redis.call('HSET', jobKey, 'Status', ARGV[13], 'CompletedAt', ARGV[6], 'LastError', message)
                            redis.call('SADD', prefix .. ':status:' .. ARGV[13], id)
                        else
                            redis.call('HSET', jobKey, 'Status', ARGV[3], 'ScheduledAt', ARGV[6])
                            redis.call('SADD', prefix .. ':status:' .. ARGV[3], id)
                            redis.call('ZADD', readyKey, rank(jobKey), id)
                        end
                    end
                end
            end
            reclaim(KEYS[5], KEYS[2])
            if readUnnamed then reclaim(KEYS[6], KEYS[4]) end
            promote(KEYS[1], KEYS[2], now, prefix, promoteLimit)
            if readUnnamed then promote(KEYS[3], KEYS[4], now, prefix, promoteLimit) end

            local function best(readyKey, unnamedOnly)
                local members = redis.call('ZRANGE', readyKey, 0, scan - 1, 'WITHSCORES')
                for i = 1, #members, 2 do
                    local id = members[i]
                    local jobKey = prefix .. ':job:' .. id
                    local st = redis.call('HGET', jobKey, 'Status')
                    if not runnable(st) then
                        redis.call('ZREM', readyKey, id)
                    else
                        if not unnamedOnly then return id, tonumber(members[i + 1]) end
                        local q = redis.call('HGET', jobKey, 'QueueName')
                        if not q or q == '' then return id, tonumber(members[i + 1]) end
                    end
                end
                return nil, nil
            end

            local id, score = best(KEYS[2], false)
            local readyKey = KEYS[2]
            local processingKey = KEYS[5]
            if readUnnamed then
                local uid, uscore = best(KEYS[4], true)
                if uid and (score == nil or uscore < score) then
                    id = uid
                    readyKey = KEYS[4]
                    processingKey = KEYS[6]
                end
            end
            if id == nil then return nil end

            redis.call('ZREM', readyKey, id)
            local jobKey = prefix .. ':job:' .. id
            local oldStatus = redis.call('HGET', jobKey, 'Status')
            redis.call('SREM', prefix .. ':status:' .. oldStatus, id)
            local attempt = tonumber(redis.call('HGET', jobKey, 'AttemptCount') or '0') + 1
            redis.call('HSET', jobKey, 'Status', ARGV[4], 'AttemptCount', attempt, 'LastAttemptAt', ARGV[6])
            redis.call('SADD', prefix .. ':status:' .. ARGV[4], id)
            redis.call('ZADD', processingKey, now, id)
            return id
        ";

        // Enqueue (SH-M016): the hash, its status set and — for a runnable job — the delayed set, in one step. A job
        // stored under the same id before is replaced whole, and its old status-set membership removed.
        // KEYS: [1] job hash, [2] delayed set. ARGV: [1] prefix, [2] Pending int, [3] Scheduled int, [4] id, [5] status,
        // [6] due score, then field/value pairs.
        private const string EnqueueScript = RankLua + @"
            local prefix = ARGV[1]
            local id = ARGV[4]
            local old = redis.call('HGET', KEYS[1], 'Status')
            if old then redis.call('SREM', prefix .. ':status:' .. old, id) end
            redis.call('DEL', KEYS[1])
            local fields = {}
            for i = 7, #ARGV do fields[#fields + 1] = ARGV[i] end
            redis.call('HSET', KEYS[1], unpack(fields))
            redis.call('SADD', prefix .. ':status:' .. ARGV[5], id)
            if runnable(ARGV[5]) then redis.call('ZADD', KEYS[2], ARGV[6], id) end
            return 1
        ";

        // Complete (SH-M017): only a Processing job, hash and status sets in one step.
        // KEYS: [1] job hash. ARGV: [1] prefix, [2] id, [3] Processing int, [4] Completed int, [5] CompletedAt ticks.
        private const string CompleteScript = @"
            if redis.call('HGET', KEYS[1], 'Status') ~= ARGV[3] then return 0 end
            local q = redis.call('HGET', KEYS[1], 'QueueName')
            if not q or q == '' then q = 'default' end
            redis.call('ZREM', ARGV[1] .. ':processing:' .. q, ARGV[2])
            redis.call('SREM', ARGV[1] .. ':status:' .. ARGV[3], ARGV[2])
            redis.call('HSET', KEYS[1], 'Status', ARGV[4], 'CompletedAt', ARGV[5])
            redis.call('SADD', ARGV[1] .. ':status:' .. ARGV[4], ARGV[2])
            return 1
        ";

        // Fail (SH-M017): only the Processing attempt the caller read (SH-M013's guard on Redis); writes LastError and the
        // outcome the caller decided, and reschedules into the job's own delayed set — no whole-hash rewrite.
        // KEYS: [1] job hash. ARGV: [1] prefix, [2] id, [3] Processing int, [4] attempt read, [5] error, [6] new status,
        // [7] "ScheduledAt" or "CompletedAt", [8] that time in ticks, [9] due score ("" when not rescheduled).
        private const string FailScript = @"
            if redis.call('HGET', KEYS[1], 'Status') ~= ARGV[3] then return 0 end
            if redis.call('HGET', KEYS[1], 'AttemptCount') ~= ARGV[4] then return 0 end
            local q = redis.call('HGET', KEYS[1], 'QueueName')
            if not q or q == '' then q = 'default' end
            redis.call('ZREM', ARGV[1] .. ':processing:' .. q, ARGV[2])
            redis.call('SREM', ARGV[1] .. ':status:' .. ARGV[3], ARGV[2])
            redis.call('HSET', KEYS[1], 'Status', ARGV[6], 'LastError', ARGV[5], ARGV[7], ARGV[8])
            redis.call('SADD', ARGV[1] .. ':status:' .. ARGV[6], ARGV[2])
            if ARGV[9] ~= '' then
                redis.call('ZADD', ARGV[1] .. ':queue:' .. q, ARGV[9], ARGV[2])
            end
            return 1
        ";

        // Cancel (SH-M018): the status check and the flip are one step, so a claim cannot land in between.
        // KEYS: [1] job hash. ARGV: [1] prefix, [2] id, [3] Pending int, [4] Scheduled int, [5] Cancelled int,
        // [6] CompletedAt ticks.
        private const string CancelScript = @"
            local st = redis.call('HGET', KEYS[1], 'Status')
            if st ~= ARGV[3] and st ~= ARGV[4] then return 0 end
            local q = redis.call('HGET', KEYS[1], 'QueueName')
            if not q or q == '' then q = 'default' end
            redis.call('ZREM', ARGV[1] .. ':queue:' .. q, ARGV[2])
            redis.call('ZREM', ARGV[1] .. ':ready:' .. q, ARGV[2])
            redis.call('SREM', ARGV[1] .. ':status:' .. st, ARGV[2])
            redis.call('HSET', KEYS[1], 'Status', ARGV[5], 'CompletedAt', ARGV[6])
            redis.call('SADD', ARGV[1] .. ':status:' .. ARGV[5], ARGV[2])
            return 1
        ";

        /// <summary>Due members one dequeue moves from a delayed set to its ready set, per key.</summary>
        private const int PromoteLimit = 100;

        /// <summary>
        /// Creates a new Redis job queue with connection settings.
        /// </summary>
        /// <param name="settings">Redis connection settings.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public RedisJobQueue(RedisSettings settings, RetryPolicy? retryPolicy = null, ISerializer? serializer = null, TimeSpan? claimTimeout = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionManager = new RedisConnectionManager(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _serializer = serializer ?? new SystemJsonSerializer();
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
            _ownsConnection = true;
        }

        /// <summary>
        /// Creates a new Redis job queue from an existing connection manager.
        /// </summary>
        /// <param name="connectionManager">A pre-configured connection manager.</param>
        /// <param name="settings">Redis settings (for key prefix configuration).</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        /// <param name="serializer">Serializer for metadata. Defaults to SystemJsonSerializer.</param>
        public RedisJobQueue(RedisConnectionManager connectionManager, RedisSettings settings, RetryPolicy? retryPolicy = null, ISerializer? serializer = null, TimeSpan? claimTimeout = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _serializer = serializer ?? new SystemJsonSerializer();
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
            _ownsConnection = false;
        }

        /// <summary>
        /// Gets the underlying connection manager for advanced scenarios.
        /// </summary>
        public RedisConnectionManager ConnectionManager => _connectionManager;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JobEnqueue.ResolveDelay(descriptor); // SH-L013
            var db = _connectionManager.GetDatabase();
            var args = new List<RedisValue>
            {
                KeyPrefix, (int)JobStatus.Pending, (int)JobStatus.Scheduled, descriptor.Guid.ToString(), (int)descriptor.Status,
                GetDueScore(descriptor.ScheduledAt ?? descriptor.EnqueuedAt),
            };
            foreach (var field in SerializeDescriptor(descriptor))
            {
                args.Add(field.Name);
                args.Add(field.Value);
            }

            await db.ScriptEvaluateAsync(EnqueueScript,
                new RedisKey[] { GetJobKey(descriptor.Guid), GetQueueKey(descriptor.QueueName) },
                args.ToArray()).ConfigureAwait(false);
            return descriptor.Guid;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            var readUnnamed = GetQueueKey(queueName) != GetQueueKey(null);
            // The threshold is on the SAME scale as the delayed score (CR-C02).
            var utcNow = DateTime.UtcNow;
            var now = GetDequeueThreshold(utcNow);

            var result = await db.ScriptEvaluateAsync(
                DequeueScript,
                new RedisKey[]
                {
                    GetQueueKey(queueName), GetReadyKey(queueName), GetQueueKey(null), GetReadyKey(null),
                    GetProcessingKey(queueName), GetProcessingKey(null),
                },
                new RedisValue[]
                {
                    KeyPrefix, (int)JobStatus.Pending, (int)JobStatus.Scheduled, (int)JobStatus.Processing, now,
                    utcNow.Ticks, readUnnamed ? "1" : "0", UnnamedScanLimit, PromoteLimit,
                    GetDequeueThreshold(utcNow - _claimTimeout), _retryPolicy.MaxRetries, JobReclaim.AbandonedErrorTemplate(_claimTimeout),
                    (int)JobStatus.Dead,
                }
            ).ConfigureAwait(false);

            if (result.IsNull || !Guid.TryParse(result.ToString(), out var jobId))
            {
                return null;
            }

            // The script already flipped the hash to Processing; just read the current state back.
            var fields = await db.HashGetAllAsync(GetJobKey(jobId)).ConfigureAwait(false);
            return fields.Length == 0 ? null : DeserializeDescriptor(fields);
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            await db.ScriptEvaluateAsync(CompleteScript,
                new RedisKey[] { GetJobKey(jobId) },
                new RedisValue[] { KeyPrefix, jobId.ToString(), (int)JobStatus.Processing, (int)JobStatus.Completed, DateTime.UtcNow.Ticks }
            ).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            var jobKey = GetJobKey(jobId);
            var fields = await db.HashGetAllAsync(jobKey).ConfigureAwait(false);
            if (fields.Length == 0)
            {
                return;
            }

            // The retry decision needs the policy, so it is made here on what was read; the script applies it only if the
            // job is still the Processing attempt that was read.
            var descriptor = DeserializeDescriptor(fields);
            if (descriptor.Status != JobStatus.Processing)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var retry = descriptor.AttemptCount < _retryPolicy.MaxRetriesFor(descriptor.MaxRetries);
            var at = retry ? now.Add(_retryPolicy.GetDelay(descriptor.AttemptCount)) : now;
            await db.ScriptEvaluateAsync(FailScript,
                new RedisKey[] { jobKey },
                new RedisValue[]
                {
                    KeyPrefix, jobId.ToString(), (int)JobStatus.Processing, descriptor.AttemptCount, error,
                    (int)(retry ? JobStatus.Scheduled : JobStatus.Dead), retry ? "ScheduledAt" : "CompletedAt", at.Ticks,
                    retry ? GetDueScore(at) : RedisValue.EmptyString,
                }
            ).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            var result = await db.ScriptEvaluateAsync(CancelScript,
                new RedisKey[] { GetJobKey(jobId) },
                new RedisValue[]
                {
                    KeyPrefix, jobId.ToString(), (int)JobStatus.Pending, (int)JobStatus.Scheduled, (int)JobStatus.Cancelled,
                    DateTime.UtcNow.Ticks,
                }
            ).ConfigureAwait(false);
            return (int)result == 1;
        }

        public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            var statusKey = GetStatusKey(status);

            var members = await db.SetMembersAsync(statusKey).ConfigureAwait(false);

            // Redis sets are unordered, so materialize ALL members first, then order by EnqueuedAt and
            // take the newest `limit` — truncating before ordering returned an arbitrary subset (CR-M023).
            // SH-L017: the hashes are requested together, so the connection pipelines them; one awaited round trip per
            // member made a large status set cost one network round trip per job. It still reads every member: the set
            // has no order to push the limit into (a sorted index would change the key layout; not done here).
            var ids = members.Select(m => Guid.TryParse(m.ToString(), out var id) ? id : (Guid?)null).Where(id => id != null).Select(id => id!.Value).ToList();
            cancellationToken.ThrowIfCancellationRequested();
            var reads = ids.Select(id => db.HashGetAllAsync(GetJobKey(id))).ToList();
            var hashes = await Task.WhenAll(reads).ConfigureAwait(false);

            var result = new List<JobDescriptor>();
            for (var i = 0; i < hashes.Length; i++)
            {
                if (hashes[i].Length > 0)
                {
                    result.Add(DeserializeDescriptor(hashes[i]));
                }
            }

            return result.OrderByDescending(j => j.EnqueuedAt).Take(limit).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _connectionManager.GetDatabase();
            var cutoff = DateTime.UtcNow.Subtract(olderThan);
            var count = 0;

            foreach (var status in new[] { JobStatus.Completed, JobStatus.Dead, JobStatus.Cancelled })
            {
                var statusKey = GetStatusKey(status);
                var members = await db.SetMembersAsync(statusKey).ConfigureAwait(false);

                foreach (var member in members)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

        /// <summary>The claimed jobs of a queue, scored by claim time, so an abandoned one can be found (TASK-664).</summary>
        private string GetProcessingKey(string? queueName) =>
            queueName != null
                ? $"{KeyPrefix}:processing:{queueName}"
                : $"{KeyPrefix}:processing:default";

        /// <summary>The ready set of a queue: due jobs ranked by priority, then age (TASK-663).</summary>
        private string GetReadyKey(string? queueName) =>
            queueName != null
                ? $"{KeyPrefix}:ready:{queueName}"
                : $"{KeyPrefix}:ready:default";

        #endregion

        #region Scoring

        // The dequeue threshold uses this exact time encoding (scheduledAt.Ticks / TimeScale). Keep
        // the two in lock-step — a mismatch is what made every future-scheduled job dequeue
        // immediately (CR-C02).
        private const double TimeScale = 1e4; // ticks -> 0.1ms units (keeps values in double's precise range)

        /// <summary>
        /// The delayed-set score: the time the job becomes due. Priority is not in it; the dequeue ranks due jobs by
        /// priority in the ready set (TASK-663), so a high priority can never pull a future job forward.
        /// </summary>
        private static double GetDueScore(DateTime dueAt) => dueAt.Ticks / TimeScale;

        /// <summary>
        /// The dequeue eligibility threshold for "now": a job is eligible once its scheduled time has
        /// passed, regardless of priority. Encoded on the same scale as <see cref="GetQueueScore"/>'s
        /// time component so the comparison is meaningful.
        /// </summary>
        private static double GetDequeueThreshold(DateTime now)
        {
            return now.Ticks / TimeScale;
        }

        #endregion

        #region Serialization

        private HashEntry[] SerializeDescriptor(JobDescriptor descriptor)
        {
            var entries = new List<HashEntry>
            {
                new("Id", descriptor.Guid.ToString()),
                new("JobType", descriptor.JobType),
                new("Status", (int)descriptor.Status),
                new("Priority", descriptor.Priority),
                new("MaxRetries", descriptor.MaxRetries is { } max ? max : RedisValue.EmptyString), // "" = null (TASK-662)
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
                entries.Add(new HashEntry("MetadataJson", _serializer.Serialize(descriptor.Metadata)));

            return entries.ToArray();
        }

        private static readonly string[] RequiredFields = { "Id", "JobType", "Status", "Priority", "MaxRetries", "AttemptCount", "EnqueuedAt" };

        private JobDescriptor DeserializeDescriptor(HashEntry[] fields)
        {
            var dict = fields.ToDictionary(f => f.Name.ToString(), f => f.Value);
            // SH-L015: a hash missing a required field is named, with the field, instead of an anonymous
            // KeyNotFoundException. Enqueue writes the hash in one script since TASK-663, so only an outside change
            // (a manual edit, an EXPIRE) can leave one; it is reported, not skipped, so it is not mistaken for absent.
            foreach (var required in RequiredFields)
            {
                if (!dict.ContainsKey(required))
                {
                    var id = dict.TryGetValue("Id", out var idValue) ? idValue.ToString() : "?";
                    throw new InvalidOperationException(
                        $"The Redis job hash {KeyPrefix}:job:{id} has no '{required}' field; it was not written by EnqueueAsync or was changed since.");
                }
            }

            var descriptor = new JobDescriptor
            {
                Guid = Guid.Parse(dict["Id"].ToString()),
                JobType = dict["JobType"]!,
                Status = (JobStatus)(int)dict["Status"],
                Priority = (int)dict["Priority"],
                MaxRetries = dict["MaxRetries"].IsNullOrEmpty ? null : (int)dict["MaxRetries"],
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
                var metadata = _serializer.Deserialize<Dictionary<string, string>>(metadataJson.ToString());
                if (metadata != null)
                {
                    descriptor.Metadata = metadata;
                }
            }

            return descriptor;
        }

        #endregion
    }
}
