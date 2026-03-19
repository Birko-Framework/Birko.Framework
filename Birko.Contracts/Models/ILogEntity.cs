namespace Birko.Data.Models
{
    /// <summary>
    /// Entity with Guid and timestamp tracking.
    /// Implemented by both AbstractLogModel and LogViewModel to enable
    /// bidirectional mapping without circular type references.
    /// </summary>
    public interface ILogEntity : IGuidEntity, ITimestamped
    {
    }
}
