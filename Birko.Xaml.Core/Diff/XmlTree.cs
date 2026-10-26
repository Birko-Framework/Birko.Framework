using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Birko.Xaml.Core.Diff;

/// <summary>Options of <see cref="XmlTree"/> (web <c>XmlTreeOptions</c>).</summary>
public sealed record XmlTreeOptions
{
    /// <summary>Child names that make a single-child element a list. <c>null</c>: <see cref="XmlTree.DefaultSimpleItemNames"/>.</summary>
    public IReadOnlyCollection<string>? SimpleItemNames { get; init; }

    /// <summary><c>xsi:nil="true"</c> reads as <c>null</c> (default); <c>false</c> reads it as an empty element.</summary>
    public bool NilAsNull { get; init; } = true;

    /// <summary>Prefix of an attribute's key.</summary>
    public string AttributePrefix { get; init; } = "@";

    /// <summary>Key of an element's text where it also has attributes or child elements. Text is never dropped.</summary>
    public string TextKey { get; init; } = "#text";
}

/// <summary>One line of <see cref="XmlTree.ToLines"/>: its text and the tree paths it shows (an element, and its attributes).</summary>
public sealed record XmlLine(string Text, IReadOnlyList<IReadOnlyList<JsonPathSegment>> Paths);

/// <summary>
/// An XML body as a JSON-like tree, so <see cref="JsonDiff"/> can compare XML responses — and the same body as
/// pretty-printed lines that know which tree path they show, so a viewer can mark the original XML with the diff's
/// result. A port of Birko.Web.Core's <c>xml-tree.ts</c>, pinned by the shared vectors.
/// </summary>
/// <remarks>
/// The tree is the ROOT element's content: an element is a field, a text-only element a string (values stay text), an
/// empty element <c>""</c>, <c>xsi:nil="true"</c> null, an attribute <c>@name</c>. Namespace declarations and
/// <c>xsi:*</c> attributes are dropped. A list is an element whose children all share one name and are several, or
/// complex, or a simple array item name (<c>string</c>, <c>int</c>, …). A name repeated among other fields keeps every
/// occurrence, as an array. Every value is a string, so compare XML with XML.
/// A DTD is ignored and never resolved (no external entities are fetched).
/// </remarks>
public static class XmlTree
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>The item names XmlSerializer writes for arrays of simple values (<c>string[]</c> → <c>&lt;string&gt;</c>).</summary>
    public static IReadOnlyList<string> DefaultSimpleItemNames { get; } = ["string", "int", "long", "decimal", "double", "boolean", "dateTime"];

    private static readonly Regex Declaration = new(@"^\s*(<\?xml[^?]*\?>)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Converts an XML body to a JSON-like tree. Returns <c>null</c> when the text is not XML — and also for a nil
    /// root, as on the web; use <see cref="TryToTree"/> to tell the two apart.
    /// </summary>
    public static JsonNode? ToTree(string text, XmlTreeOptions? options = null) =>
        TryToTree(text, out var tree, options) ? tree : null;

    /// <summary>Converts an XML body to a JSON-like tree; <c>false</c> when the text is not XML.</summary>
    public static bool TryToTree(string text, out JsonNode? tree, XmlTreeOptions? options = null)
    {
        tree = null;
        var root = Parse(text);
        if (root is null) return false;
        tree = Value(root, new Resolved(options));
        return true;
    }

    /// <summary>
    /// Pretty-prints an XML body, two spaces per level, one line per element (a text-only element stays on one line),
    /// and tags each line with the <see cref="ToTree"/> paths it shows. Returns <c>null</c> when the text is not XML.
    /// Comments and processing instructions are kept and belong to no path.
    /// </summary>
    public static IReadOnlyList<XmlLine>? ToLines(string text, XmlTreeOptions? options = null)
    {
        var root = Parse(text);
        if (root is null) return null;
        var o = new Resolved(options);
        var lines = new List<XmlLine>();
        var decl = Declaration.Match(text);
        if (decl.Success) lines.Add(new XmlLine(decl.Groups[1].Value, []));
        Walk(root, [], "", o, lines);
        return lines;
    }

    private sealed class Resolved(XmlTreeOptions? options)
    {
        public HashSet<string> Simple { get; } = new(options?.SimpleItemNames ?? DefaultSimpleItemNames, StringComparer.Ordinal);
        public bool NilAsNull { get; } = options?.NilAsNull ?? true;
        public string Attr { get; } = options?.AttributePrefix ?? "@";
        public string Text { get; } = options?.TextKey ?? "#text";
    }

    private static XElement? Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static List<XAttribute> DataAttributes(XElement el) =>
        el.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.Namespace != Xsi).ToList();

    private static bool IsNil(XElement el, Resolved o) => o.NilAsNull && (string?)el.Attribute(Xsi + "nil") == "true";

    /// <summary>Text directly inside the element (not in its child elements), trimmed.</summary>
    private static string OwnText(XElement el) =>
        string.Concat(el.Nodes().OfType<XText>().Select(t => t.Value)).Trim();

    private static bool IsList(List<XElement> children, Resolved o)
    {
        if (children.Count == 0) return false;
        var name = children[0].Name.LocalName;
        if (!children.All(c => c.Name.LocalName == name)) return false;
        return children.Count > 1 || children[0].HasElements || o.Simple.Contains(name);
    }

    private enum ShapeKind { Nil, Text, List, Object }

    private sealed record Shape(ShapeKind Kind, List<XElement> Children, List<XAttribute> Attrs, string Text,
        List<(string Name, List<XElement> Elements)> Fields);

    private static Shape ShapeOf(XElement el, Resolved o)
    {
        var none = new List<XElement>();
        if (IsNil(el, o)) return new Shape(ShapeKind.Nil, none, [], "", []);
        var children = el.Elements().ToList();
        var attrs = DataAttributes(el);
        if (children.Count == 0 && attrs.Count == 0) return new Shape(ShapeKind.Text, children, attrs, "", []);
        var text = OwnText(el);
        if (attrs.Count == 0 && text.Length == 0 && IsList(children, o)) return new Shape(ShapeKind.List, children, attrs, text, []);
        // Insertion-ordered, as the web's Map: a field sits where its name first appears.
        var fields = new List<(string Name, List<XElement> Elements)>();
        foreach (var c in children)
        {
            var name = c.Name.LocalName;
            var at = fields.FindIndex(f => f.Name == name);
            if (at >= 0) fields[at].Elements.Add(c);
            else fields.Add((name, [c]));
        }
        return new Shape(ShapeKind.Object, children, attrs, text, fields);
    }

    private static JsonNode? Value(XElement el, Resolved o)
    {
        var s = ShapeOf(el, o);
        switch (s.Kind)
        {
            case ShapeKind.Nil: return null;
            case ShapeKind.Text: return JsonValue.Create(el.Value);
            case ShapeKind.List:
            {
                var list = new JsonArray();
                foreach (var c in s.Children) list.Add(Value(c, o));
                return list;
            }
        }
        var obj = new JsonObject();
        foreach (var a in s.Attrs) obj[o.Attr + a.Name.LocalName] = a.Value;
        if (s.Text.Length > 0) obj[o.Text] = s.Text;
        foreach (var (name, els) in s.Fields)
        {
            if (els.Count == 1) { obj[name] = Value(els[0], o); continue; }
            var many = new JsonArray();
            foreach (var c in els) many.Add(Value(c, o));
            obj[name] = many;
        }
        return obj;
    }

    private static string EscText(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscAttr(string s) => EscText(s).Replace("\"", "&quot;");

    /// <summary>The name as written: <c>prefix:local</c> where the source used a prefix.</summary>
    private static string Qualified(XName name, XElement scope, bool isAttribute)
    {
        if (name.Namespace == XNamespace.None) return name.LocalName;
        if (name.Namespace == XNamespace.Xmlns) return "xmlns:" + name.LocalName;
        var prefix = scope.GetPrefixOfNamespace(name.Namespace);
        // An element in the default namespace has no prefix; an attribute never takes the default namespace.
        return string.IsNullOrEmpty(prefix) ? (isAttribute ? name.ToString() : name.LocalName) : $"{prefix}:{name.LocalName}";
    }

    private static string OpenTag(XElement el, string close)
    {
        var sb = new StringBuilder("<").Append(Qualified(el.Name, el, false));
        foreach (var a in el.Attributes()) sb.Append(' ').Append(Qualified(a.Name, el, true)).Append("=\"").Append(EscAttr(a.Value)).Append('"');
        return sb.Append(close).ToString();
    }

    private static string CloseTag(XElement el) => $"</{Qualified(el.Name, el, false)}>";

    private static bool IsText(XNode n) => n is XText; // XCData is an XText

    private static string NodeText(XNode n) => n is XCData c ? $"<![CDATA[{c.Value}]]>" : EscText(((XText)n).Value.Trim());

    private static void Walk(XElement el, JsonPathSegment[] path, string indent, Resolved o, List<XmlLine> lines)
    {
        var s = ShapeOf(el, o);
        var own = new List<IReadOnlyList<JsonPathSegment>> { path };
        if (!IsNil(el, o)) own.AddRange(DataAttributes(el).Select(a => (IReadOnlyList<JsonPathSegment>)[.. path, o.Attr + a.Name.LocalName]));
        var nodes = el.Nodes().Where(n => n is XElement or XComment or XProcessingInstruction || (n is XText t && t.Value.Trim().Length > 0)).ToList();
        if (nodes.Count == 0)
        {
            lines.Add(new XmlLine(indent + OpenTag(el, "/>"), own));
            return;
        }
        if (!el.HasElements && nodes.All(IsText))
        {
            var inner = string.Concat(nodes.Select(NodeText));
            if (s.Kind == ShapeKind.Object) own.Add([.. path, o.Text]);
            lines.Add(new XmlLine($"{indent}{OpenTag(el, ">")}{inner}{CloseTag(el)}", own));
            return;
        }
        lines.Add(new XmlLine(indent + OpenTag(el, ">"), own));
        var childIndent = indent + "  ";
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        foreach (var n in nodes)
        {
            switch (n)
            {
                case XElement c:
                {
                    JsonPathSegment[] childPath;
                    if (s.Kind == ShapeKind.List) childPath = [.. path, index++];
                    else if (s.Kind == ShapeKind.Object)
                    {
                        var name = c.Name.LocalName;
                        var k = seen.GetValueOrDefault(name);
                        seen[name] = k + 1;
                        var count = s.Fields.FirstOrDefault(f => f.Name == name).Elements?.Count ?? 0;
                        childPath = count > 1 ? [.. path, name, k] : [.. path, name];
                    }
                    else childPath = path; // nil: the children are not in the tree; mark them with the element
                    Walk(c, childPath, childIndent, o, lines);
                    break;
                }
                case XComment cm:
                    lines.Add(new XmlLine($"{childIndent}<!--{cm.Value}-->", []));
                    break;
                case XProcessingInstruction pi:
                    lines.Add(new XmlLine($"{childIndent}<?{pi.Target} {pi.Data}?>", []));
                    break;
                default:
                    lines.Add(new XmlLine(childIndent + NodeText(n),
                        s.Kind == ShapeKind.Object ? [(IReadOnlyList<JsonPathSegment>)[.. path, o.Text]] : [path]));
                    break;
            }
        }
        lines.Add(new XmlLine(indent + CloseTag(el), [path]));
    }
}
