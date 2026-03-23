using System;
using System.Collections.Generic;

namespace Birko.Structures.Tries;

/// <summary>
/// Compressed trie (radix tree / Patricia tree).
/// Edges store multi-character labels, reducing node count for sparse key sets.
/// </summary>
public class CompressedTrie
{
    private sealed class RadixNode
    {
        public Dictionary<char, (string Label, RadixNode Node)> Children { get; } = new();
        public bool IsEndOfWord { get; set; }
    }

    private readonly RadixNode _root = new();

    /// <summary>
    /// Gets the number of words stored.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Inserts a word into the compressed trie.
    /// </summary>
    public void Insert(string word)
    {
        if (string.IsNullOrEmpty(word)) return;

        if (InsertInternal(_root, word))
        {
            Count++;
        }
    }

    /// <summary>
    /// Checks if an exact word exists.
    /// </summary>
    public bool Search(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        return SearchInternal(_root, word, exactMatch: true);
    }

    /// <summary>
    /// Checks if any word starts with the given prefix.
    /// </summary>
    public bool StartsWith(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return Count > 0;
        return SearchInternal(_root, prefix, exactMatch: false);
    }

    /// <summary>
    /// Gets all words with the given prefix.
    /// </summary>
    public IReadOnlyList<string> GetWordsWithPrefix(string prefix)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(prefix))
        {
            CollectWords(_root, string.Empty, results);
            return results;
        }

        var (node, remaining) = FindNodeForPrefix(_root, prefix);
        if (node == null) return results;

        CollectWords(node, prefix[..^remaining.Length] + (remaining.Length > 0 ? "" : ""), results);
        return results;
    }

    /// <summary>
    /// Removes a word. Returns true if found and removed.
    /// </summary>
    public bool Remove(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        if (RemoveInternal(_root, word))
        {
            Count--;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets all words.
    /// </summary>
    public IReadOnlyList<string> GetAllWords() => GetWordsWithPrefix(string.Empty);

    private static bool InsertInternal(RadixNode node, string remaining)
    {
        if (remaining.Length == 0)
        {
            if (node.IsEndOfWord) return false;
            node.IsEndOfWord = true;
            return true;
        }

        var firstChar = remaining[0];

        if (!node.Children.TryGetValue(firstChar, out var entry))
        {
            var newNode = new RadixNode { IsEndOfWord = true };
            node.Children[firstChar] = (remaining, newNode);
            return true;
        }

        var label = entry.Label;
        var child = entry.Node;
        int commonLen = CommonPrefixLength(label, remaining);

        if (commonLen == label.Length && commonLen == remaining.Length)
        {
            if (child.IsEndOfWord) return false;
            child.IsEndOfWord = true;
            return true;
        }

        if (commonLen == label.Length)
        {
            return InsertInternal(child, remaining[commonLen..]);
        }

        // Split the edge
        var splitNode = new RadixNode();
        var labelSuffix = label[commonLen..];
        var remainingSuffix = remaining[commonLen..];

        splitNode.Children[labelSuffix[0]] = (labelSuffix, child);
        node.Children[firstChar] = (label[..commonLen], splitNode);

        if (remainingSuffix.Length == 0)
        {
            splitNode.IsEndOfWord = true;
        }
        else
        {
            var newNode = new RadixNode { IsEndOfWord = true };
            splitNode.Children[remainingSuffix[0]] = (remainingSuffix, newNode);
        }

        return true;
    }

    private static bool SearchInternal(RadixNode node, string remaining, bool exactMatch)
    {
        if (remaining.Length == 0)
        {
            return exactMatch ? node.IsEndOfWord : true;
        }

        var firstChar = remaining[0];
        if (!node.Children.TryGetValue(firstChar, out var entry)) return false;

        var label = entry.Label;
        var child = entry.Node;
        int commonLen = CommonPrefixLength(label, remaining);

        if (commonLen == remaining.Length)
        {
            return exactMatch ? (commonLen == label.Length && child.IsEndOfWord) : true;
        }

        if (commonLen < label.Length) return false;

        return SearchInternal(child, remaining[commonLen..], exactMatch);
    }

    private static bool RemoveInternal(RadixNode node, string remaining)
    {
        if (remaining.Length == 0)
        {
            if (!node.IsEndOfWord) return false;
            node.IsEndOfWord = false;
            return true;
        }

        var firstChar = remaining[0];
        if (!node.Children.TryGetValue(firstChar, out var entry)) return false;

        var label = entry.Label;
        var child = entry.Node;

        if (remaining.Length < label.Length || !remaining.StartsWith(label)) return false;

        if (remaining.Length == label.Length)
        {
            if (!child.IsEndOfWord) return false;
            child.IsEndOfWord = false;

            if (child.Children.Count == 0)
            {
                node.Children.Remove(firstChar);
            }
            else if (child.Children.Count == 1)
            {
                MergeWithChild(node, firstChar, label, child);
            }

            return true;
        }

        var removed = RemoveInternal(child, remaining[label.Length..]);
        if (removed && !child.IsEndOfWord && child.Children.Count == 1)
        {
            MergeWithChild(node, firstChar, label, child);
        }
        else if (removed && !child.IsEndOfWord && child.Children.Count == 0)
        {
            node.Children.Remove(firstChar);
        }

        return removed;
    }

    private static void MergeWithChild(RadixNode parent, char key, string label, RadixNode node)
    {
        foreach (var (childKey, childEntry) in node.Children)
        {
            parent.Children[key] = (label + childEntry.Label, childEntry.Node);
            break;
        }
    }

    private static (RadixNode? Node, string Remaining) FindNodeForPrefix(RadixNode node, string prefix)
    {
        if (prefix.Length == 0) return (node, string.Empty);

        var firstChar = prefix[0];
        if (!node.Children.TryGetValue(firstChar, out var entry)) return (null, prefix);

        var label = entry.Label;
        var child = entry.Node;
        int commonLen = CommonPrefixLength(label, prefix);

        if (commonLen == prefix.Length) return (child, string.Empty);
        if (commonLen < label.Length) return (commonLen == prefix.Length ? (child, string.Empty) : (null, prefix));

        return FindNodeForPrefix(child, prefix[commonLen..]);
    }

    private static void CollectWords(RadixNode node, string prefix, List<string> results)
    {
        if (node.IsEndOfWord)
        {
            results.Add(prefix);
        }

        foreach (var (_, (label, child)) in node.Children)
        {
            CollectWords(child, prefix + label, results);
        }
    }

    private static int CommonPrefixLength(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return len;
    }
}
