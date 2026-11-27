using System;
using System.Threading.Tasks;
using Raven.Client.Exceptions.Database;
using Raven.Client.Exceptions.Documents.Indexes;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// Sends a store request again when RavenDB refused it for a passing reason, shared by <see cref="RavenDBStore{T}"/>
    /// and <see cref="AsyncRavenDBStore{T}"/> (TASK-998). Every query and every <c>SaveChanges</c> runs through it.
    /// </summary>
    /// <remarks>
    /// Both refusals mean the server applied nothing, so sending the request again is safe (rule 66):
    /// <list type="bullet">
    /// <item><see cref="IndexCreationException"/>: a dynamic query makes the server create an auto index, and on a new
    /// database, while other clients query and save, that sometimes timed out after 15 s. The TASK-995 RavenDB contract
    /// failed this way 3 times in 30 runs. Retried up to <see cref="MaxIndexCreationAttempts"/> times.</item>
    /// <item><see cref="DatabaseDisabledException"/> ("unloaded and locked"): a create of the database sent by another
    /// client makes the server unload and lock it briefly, even while it is already in use. TASK-996's start-up check
    /// cannot prevent a create that arrives after it. Retried, briefly spaced, until <see cref="DisabledWait"/> has
    /// passed.</item>
    /// </list>
    /// Any other exception, or the last attempt's, is thrown.
    /// </remarks>
    internal static class RavenQueryRetry
    {
        /// <summary>How many times a query is sent when its auto index cannot be created in time.</summary>
        public const int MaxIndexCreationAttempts = 3;

        /// <summary>How long a request refused by a briefly locked database keeps being sent again.</summary>
        public static readonly TimeSpan DisabledWait = TimeSpan.FromSeconds(30);

        public static T Run<T>(Func<T> request)
        {
            var state = new State();
            while (true)
            {
                try
                {
                    return request();
                }
                catch (Exception ex) when (state.Retry(ex))
                {
                    System.Threading.Thread.Sleep(state.Pause);
                }
            }
        }

        public static void Run(Action request) => Run(() => { request(); return true; });

        public static async Task<T> RunAsync<T>(Func<Task<T>> request)
        {
            var state = new State();
            while (true)
            {
                try
                {
                    return await request().ConfigureAwait(false);
                }
                catch (Exception ex) when (state.Retry(ex))
                {
                    await Task.Delay(state.Pause).ConfigureAwait(false);
                }
            }
        }

        public static Task RunAsync(Func<Task> request) => RunAsync(async () => { await request().ConfigureAwait(false); return true; });

        private sealed class State
        {
            private readonly DateTime _until = DateTime.UtcNow + DisabledWait;
            private int _indexAttempts = 1;
            private TimeSpan _pause = TimeSpan.FromMilliseconds(50);

            public TimeSpan Pause { get; private set; }

            public bool Retry(Exception ex)
            {
                switch (ex)
                {
                    case IndexCreationException when _indexAttempts < MaxIndexCreationAttempts:
                        _indexAttempts++;
                        Pause = TimeSpan.Zero;
                        return true;
                    case DatabaseDisabledException when DateTime.UtcNow < _until:
                        Pause = _pause;
                        if (_pause < TimeSpan.FromSeconds(1)) _pause *= 2;
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
