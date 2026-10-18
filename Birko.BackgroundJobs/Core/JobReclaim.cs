using System;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Reclaiming a job a dead worker left in <see cref="JobStatus.Processing"/> (TASK-664). Every queue offers such a job
    /// again once it has been held longer than its claim timeout, guarded so that one worker wins, and buries it as
    /// <see cref="JobStatus.Dead"/> when its attempt budget is spent.
    /// </summary>
    /// <remarks>
    /// The timeout is a presumption, not knowledge: nothing tells a dead holder from a slow one, so a job that outlives it
    /// while genuinely running is handed to a second worker. It defaults to <see cref="JobQueueOptions.JobTimeout"/>'s own
    /// default, after which the processor has cancelled the job; a host that raises <c>JobTimeout</c> must raise this too.
    /// </remarks>
    public static class JobReclaim
    {
        /// <summary>The claim timeout every queue uses unless given one: 30 minutes.</summary>
        public static readonly TimeSpan DefaultClaimTimeout = TimeSpan.FromMinutes(30);

        /// <summary>
        /// The claim timeout a queue was given, or <see cref="DefaultClaimTimeout"/>. A zero or negative timeout would hand
        /// every running job to the next poll, so it is refused.
        /// </summary>
        public static TimeSpan Resolve(TimeSpan? claimTimeout)
        {
            var value = claimTimeout ?? DefaultClaimTimeout;
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(claimTimeout), value, "A claim timeout must be positive.");
            return value;
        }

        /// <summary>The <c>LastError</c> of a job buried because it was abandoned with no attempts left.</summary>
        public static string AbandonedError(TimeSpan claimTimeout, int attempts)
            => AbandonedErrorTemplate(claimTimeout).Replace(AttemptsPlaceholder, attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>The placeholder <see cref="AbandonedErrorTemplate"/> carries for the attempt count.</summary>
        public const string AttemptsPlaceholder = "{attempts}";

        /// <summary>
        /// <see cref="AbandonedError"/> with the attempt count left as <see cref="AttemptsPlaceholder"/>, for a backend that
        /// buries inside a server-side script (Redis) and fills the count in there.
        /// </summary>
        public static string AbandonedErrorTemplate(TimeSpan claimTimeout)
            => $"Abandoned: held in Processing for longer than {claimTimeout} after {AttemptsPlaceholder} attempt(s), and the attempt budget is spent.";
    }
}
