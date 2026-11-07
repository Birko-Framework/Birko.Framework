using System;
using System.Collections.Generic;

namespace Birko.Data.Views;

/// <summary>
/// Describes a field selected from a source entity into a view.
/// </summary>
public sealed class FieldSelector
{
    /// <summary>The source entity type containing the field.</summary>
    public Type SourceType { get; }

    /// <summary>
    /// The source member path joined with <c>.</c>: <c>Name</c> for a top-level property, <c>Sub.X</c> for a nested one.
    /// For one segment it is the property name, as it always was.
    /// </summary>
    public string SourceProperty { get; }

    /// <summary>
    /// The source member path, one property name per segment, from the source entity down (TASK-843):
    /// <c>r =&gt; r.Sub.X</c> is <c>["Sub", "X"]</c>. The one producer of the path every provider renders; a provider
    /// that cannot express a nested path (<see cref="IsNested"/>) refuses it by name, never reads the last segment.
    /// </summary>
    public IReadOnlyList<string> SourcePath { get; }

    /// <summary>True when <see cref="SourcePath"/> has more than one segment: a member below the source entity.</summary>
    public bool IsNested => SourcePath.Count > 1;

    /// <summary>Property name on the view result type.</summary>
    public string ViewProperty { get; }

    /// <summary>A field read from one top-level property of the source entity.</summary>
    public FieldSelector(Type sourceType, string sourceProperty, string viewProperty)
        : this(sourceType, new[] { sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty)) }, viewProperty)
    {
    }

    /// <summary>A field read from a member path of the source entity (<c>["Sub", "X"]</c>).</summary>
    public FieldSelector(Type sourceType, IReadOnlyList<string> sourcePath, string viewProperty)
    {
        if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
        if (sourcePath.Count == 0) throw new ArgumentException("A source path has at least one segment.", nameof(sourcePath));

        SourceType = sourceType;
        SourcePath = sourcePath;
        SourceProperty = sourcePath.Count == 1 ? sourcePath[0] : string.Join(".", sourcePath);
        ViewProperty = viewProperty;
    }
}
