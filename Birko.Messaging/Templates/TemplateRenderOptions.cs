using System;
using System.Collections.Generic;

namespace Birko.Messaging.Templates;

/// <summary>
/// How one render call treats merged values. Chosen per call because one model is rendered into formats that need
/// different treatment: a plain-text subject and a Markdown body.
/// </summary>
public sealed class TemplateRenderOptions
{
    /// <summary>
    /// Encodes every merged value except those in <see cref="RawPaths"/>. <c>null</c> merges values verbatim, exactly as
    /// the overload without options does.
    /// </summary>
    public ITemplateValueEncoder? Encoder { get; init; }

    /// <summary>
    /// Placeholder paths merged without the <see cref="Encoder"/>, matched by the set's own comparer; the default set is
    /// ordinal, so exact and case-sensitive (<c>"Order.TrackingUrl"</c>). Declared by the calling code, because whether a value is trusted depends on where
    /// it came from (a link the system built), which a template author cannot know. A path not listed is encoded, so a
    /// misspelt entry fails safe: the value shows as text.
    /// </summary>
    public IReadOnlySet<string> RawPaths { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
