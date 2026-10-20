namespace Birko.Data.Stores
{
    /// <summary>
    /// A soft-delete decorator: a delete through it keeps the row stored, hidden, so the row's keys stay taken. Its inner
    /// store also sees deleted rows, which is where the Sluggable probe asks, because a deleted row keeps its slug
    /// (TASK-638).
    /// </summary>
    /// <remarks>
    /// Moved here from Birko.Data.Patterns (TASK-698) so a project below that layer can recognise a soft-delete chain:
    /// Birko.Data.Localization refuses one under a translation store, whose re-created key would collide with the hidden
    /// row on every provider.
    /// </remarks>
    public interface ISoftDeleteStoreWrapper : IStoreWrapper
    {
    }
}
