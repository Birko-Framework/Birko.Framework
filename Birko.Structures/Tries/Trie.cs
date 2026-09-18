using System;
using System.Collections.Generic;

namespace Birko.Structures.Tries;

/// <summary>
/// Standard trie (prefix tree) for string keys.
/// </summary>
public class Trie
{
    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();
        public bool IsEndOfWord { get; set; }
    }

    private readonly TrieNode _root = new();

    /// <summary>
    /// Gets the number of words stored.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Inserts a word into the trie.
    /// </summary>
    public void Insert(string word)
    {
        if (string.IsNullOrEmpty(word)) return;

        var node = _root;
        foreach (var ch in word)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                child = new TrieNode();
                node.Children[ch] = child;
            }
            node = child;
        }

        if (!node.IsEndOfWord)
        {
            node.IsEndOfWord = true;
            Count++;
        }
    }

    /// <summary>
    /// Checks if an exact word exists.
    /// </summary>
    public bool Search(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        var node = FindNode(word);
        return node is { IsEndOfWord: true };
    }

    /// <summary>
    /// Checks if any word starts with the given prefix.
    /// </summary>
    public bool StartsWith(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return Count > 0;
        return FindNode(prefix) != null;
    }

    /// <summary>
    /// Gets all words with the given prefix.
    /// </summary>
    public IReadOnlyList<string> GetWordsWithPrefix(string prefix)
    {
        var results = new List<string>();
        var node = string.IsNullOrEmpty(prefix) ? _root : FindNode(prefix);
        if (node == null) return results;

        CollectWords(node, prefix ?? string.Empty, results);
        return results;
    }

    /// <summary>
    /// Removes a word from the trie.
    /// Returns true if the word was found and removed.
    /// </summary>
    public bool Remove(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (Remove(_root, word, 0))
        {
            Count--;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets all words in the trie.
    /// </summary>
    public IReadOnlyList<string> GetAllWords() => GetWordsWithPrefix(string.Empty);

    private TrieNode? FindNode(string prefix)
    {
        var node = _root;
        foreach (var ch in prefix)
        {
            if (!node.Children.TryGetValue(ch, out var child)) return null;
            node = child;
        }
        return node;
    }

    private static void CollectWords(TrieNode node, string prefix, List<string> results)
    {
        if (node.IsEndOfWord)
        {
            results.Add(prefix);
        }

        foreach (var (ch, child) in node.Children)
        {
            CollectWords(child, prefix + ch, results);
        }
    }

    private static bool Remove(TrieNode node, string word, int depth)
    {
        if (depth == word.Length)
        {
            if (!node.IsEndOfWord) return false;
            node.IsEndOfWord = false;
            return true;
        }

        var ch = word[depth];
        if (!node.Children.TryGetValue(ch, out var child)) return false;

        var removed = Remove(child, word, depth + 1);
        if (removed && !child.IsEndOfWord && child.Children.Count == 0)
        {
            node.Children.Remove(ch);
        }

        return removed;
    }
}
