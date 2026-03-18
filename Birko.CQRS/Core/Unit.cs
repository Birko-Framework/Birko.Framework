using System;

namespace Birko.CQRS
{
    /// <summary>
    /// Represents a void return type for commands that produce no result.
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
    {
        /// <summary>
        /// The single value of <see cref="Unit"/>.
        /// </summary>
        public static readonly Unit Value = default;

        /// <summary>
        /// Returns a completed task containing <see cref="Value"/>.
        /// </summary>
        public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

        public int CompareTo(Unit other) => 0;

        public bool Equals(Unit other) => true;

        public override bool Equals(object? obj) => obj is Unit;

        public override int GetHashCode() => 0;

        public override string ToString() => "()";

        public static bool operator ==(Unit left, Unit right) => true;

        public static bool operator !=(Unit left, Unit right) => false;
    }
}
