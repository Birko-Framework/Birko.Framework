namespace Birko.Data.Tagging;

/// <summary>
/// Thrown when an operation names a tag id that does not exist in the ambient tenant: <c>AttachTagAsync</c>,
/// <c>SetEntityTagsAsync</c> and <c>UpdateTagAsync</c> (TASK-147, TASK-933).
/// </summary>
/// <remarks>
/// A distinct type so a host can map exactly this refusal (to a 404, typically) without catching every
/// <see cref="InvalidOperationException"/> the attach path can raise. It derives from
/// <see cref="InvalidOperationException"/>, which these methods threw before, so existing catches keep working.
/// </remarks>
public class TagNotFoundException : InvalidOperationException
{
    public TagNotFoundException(Guid tagId)
        : base($"Tag {tagId} not found.")
    {
        TagId = tagId;
    }

    public Guid TagId { get; }
}
