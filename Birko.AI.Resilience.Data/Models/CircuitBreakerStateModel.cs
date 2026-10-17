using System;
using System.Security.Cryptography;
using System.Text;
using Birko.AI.Resilience.Stores;
using Birko.Data.Models;

namespace Birko.AI.Resilience.Data.Models
{
    /// <summary>
    /// One row per provider: the persisted state of its circuit (TASK-530). Its <see cref="AbstractModel.Guid"/> is
    /// derived from the provider name (<see cref="GuidFor"/>), so saving a provider's state again replaces its row
    /// instead of adding one.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.Table("AiCircuitBreakerStates")]
    public class CircuitBreakerStateModel : AbstractModel
    {
        public string Provider { get; set; } = string.Empty;
        public int State { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// The row id of <paramref name="provider"/>'s state: a name-based (version 3) Guid, the same in every process.
        /// </summary>
        public static Guid GuidFor(string provider)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes("Birko.AI.Resilience/circuit/" + provider));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash);
        }

        public static CircuitBreakerStateModel FromState(CircuitBreakerState state) => new()
        {
            Guid = GuidFor(state.Provider),
            Provider = state.Provider,
            State = state.State,
            ConsecutiveFailures = state.ConsecutiveFailures,
            OpenedAt = state.OpenedAt,
            LastFailureAt = state.LastFailureAt,
            UpdatedAt = state.UpdatedAt,
        };

        public CircuitBreakerState ToState() => new()
        {
            Provider = Provider,
            State = State,
            ConsecutiveFailures = ConsecutiveFailures,
            OpenedAt = OpenedAt,
            LastFailureAt = LastFailureAt,
            UpdatedAt = UpdatedAt,
        };
    }
}
