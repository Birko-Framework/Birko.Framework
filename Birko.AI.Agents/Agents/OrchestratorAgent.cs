using Birko.AI.Models;
using Birko.AI.Providers;

namespace Birko.AI.Agents
{
    /// <summary>
    /// Base class for orchestrator agents that coordinate and delegate work.
    /// Orchestrators analyze tasks, break them down, and route work to specialized agents.
    /// Examples: Dragon (requirements gathering), Wyrm (task delegation), Wyvern (project analysis).
    /// </summary>
    public abstract class OrchestratorAgent : Agent
    {
        protected OrchestratorAgent(ILlmProvider provider, AgentOptions? options = null)
            : base(provider, options)
        {
        }

        /// <summary>
        /// Common orchestration best practices and patterns.
        /// Derived classes can include this in their system prompts for consistent guidance.
        /// This is opt-in - only included if the derived class calls this method.
        /// </summary>
        protected string GetOrchestratorGuidance()
        {
            return @"
## Orchestration Best Practices:
- Analyze before acting: understand the full scope of work
- Break down complexity: divide large tasks into manageable pieces
- Delegate strategically: route work to appropriate specialized components
- Coordinate dependencies: ensure prerequisites are handled first
- Validate and iterate: check outputs and refine as needed
- Handle failures gracefully: retry with adjusted strategies when needed

## Common Orchestration Patterns:
- Task Analysis: What needs to be done? What are the sub-components?
- Routing: Which agent/tool is best suited for each component?
- Sequencing: What order should work be completed in?
- Validation: Are outputs correct and complete?";
        }

        /// <summary>
        /// Model depth-based reasoning guidance for orchestrators.
        /// Returns appropriate reasoning instructions based on the LLM's capability level.
        /// This is a simplified version compared to Agent.GetDepthGuidance() for orchestration tasks.
        /// </summary>
        protected override string GetDepthGuidance()
        {
            return Options.ModelDepth switch
            {
                <= 3 => @"
Reasoning approach: Quick and efficient
- Make direct, straightforward decisions
- Prioritize speed over exhaustive analysis",
                >= 7 => @"
Reasoning approach: Deep and thorough
- Think carefully through multiple approaches before acting
- Consider edge cases and potential issues
- Analyze trade-offs and document your reasoning",
                _ => @"
Reasoning approach: Balanced
- Think step-by-step about what you need to do
- Consider important edge cases
- Balance thoroughness with efficiency"
            };
        }

        /// <summary>
        /// Helper method to extract text content from various content block formats.
        /// Handles string, ContentBlock, IEnumerable&lt;ContentBlock&gt;, JsonElement, and dynamic objects.
        /// This robust implementation handles various serialization/deserialization scenarios.
        /// </summary>
        public static string ExtractTextFromContent(object? content)
        {
            if (content == null)
                return string.Empty;

            // Direct string content
            if (content is string text)
                return text;

            // Single ContentBlock
            if (content is ContentBlock block)
                return block.Text ?? string.Empty;

            // List<ContentBlock> or IEnumerable<ContentBlock>
            if (content is IEnumerable<ContentBlock> contentBlocks)
            {
                return string.Join("\n", contentBlocks
                    .Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text))
                    .Select(b => b.Text));
            }

            // Handle case where Content was serialized/deserialized as JsonElement
            if (content is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var texts = new List<string>();
                    foreach (var element in jsonElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("type", out var typeEl) &&
                            typeEl.GetString() == "text" &&
                            element.TryGetProperty("text", out var textEl))
                        {
                            var t = textEl.GetString();
                            if (!string.IsNullOrEmpty(t))
                                texts.Add(t);
                        }
                    }
                    return string.Join("\n", texts);
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return jsonElement.GetString() ?? string.Empty;
                }
            }

            // Handle IEnumerable<object> which might contain ContentBlocks or anonymous objects
            if (content is IEnumerable<object> objects)
            {
                var texts = new List<string>();
                foreach (var obj in objects)
                {
                    if (obj is ContentBlock cb && cb.Type == "text" && !string.IsNullOrEmpty(cb.Text))
                    {
                        texts.Add(cb.Text);
                    }
                    else if (obj is string str)
                    {
                        texts.Add(str);
                    }
                    // Check for anonymous object or dynamic with Type and Text properties
                    else if (obj != null)
                    {
                        var objType = obj.GetType();
                        var typeProp = objType.GetProperty("Type") ?? objType.GetProperty("type");
                        var textProp = objType.GetProperty("Text") ?? objType.GetProperty("text");

                        if (typeProp != null && textProp != null)
                        {
                            var typeVal = typeProp.GetValue(obj)?.ToString();
                            if (typeVal == "text")
                            {
                                var textVal = textProp.GetValue(obj)?.ToString();
                                if (!string.IsNullOrEmpty(textVal))
                                    texts.Add(textVal);
                            }
                        }
                    }
                }
                if (texts.Count > 0)
                    return string.Join("\n", texts);
            }

            // Last resort: if content looks like it might already be JSON or text, return it
            // But NEVER call ToString() on collection types as that produces type names
            var contentType = content.GetType();
            if (contentType.IsGenericType &&
                (contentType.GetGenericTypeDefinition() == typeof(List<>) ||
                 contentType.Name.Contains("Enumerable")))
            {
                // This is a collection we couldn't process - return empty rather than type name
                return string.Empty;
            }

            // For primitive types and simple objects, ToString is safe
            return content.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Extracts and cleans JSON from a response that may contain markdown code blocks.
        /// Useful for orchestrators that need to parse structured output from LLMs.
        /// Uses regex patterns to handle various formatting scenarios.
        /// </summary>
        public static string ExtractJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Agent returned empty response.");

            // If it already starts with '{', extract balanced JSON
            var trimmed = content.Trim();

            // Check if the response contains an error message before attempting JSON extraction
            if (trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"LLM request failed: {content}");

            if (trimmed.StartsWith('{'))
                return ExtractBalancedJson(trimmed, 0);

            // Try to extract from markdown code block (```json ... ``` or ``` ... ```)
            var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(
                content,
                @"```(?:json)?\s*\n?([\s\S]*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (codeBlockMatch.Success)
            {
                var blockContent = codeBlockMatch.Groups[1].Value.Trim();
                if (blockContent.StartsWith('{'))
                    return ExtractBalancedJson(blockContent, 0);
            }

            // Try to find JSON object anywhere in the text using bracket matching
            var startIdx = content.IndexOf('{');
            if (startIdx >= 0)
                return ExtractBalancedJson(content, startIdx);

            var preview = content.Length > 100 ? content[..100] + "..." : content;
            throw new InvalidOperationException($"Agent did not return valid JSON. Response started with: {preview}");
        }

        /// <summary>
        /// Extracts a balanced JSON object by counting braces.
        /// Handles nested objects, arrays, and string escaping correctly.
        /// </summary>
        private static string ExtractBalancedJson(string content, int startIndex)
        {
            if (startIndex >= content.Length || content[startIndex] != '{')
                throw new InvalidOperationException("Could not extract JSON from response.");

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex; i < content.Length; i++)
            {
                char c = content[i];

                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return content.Substring(startIndex, i - startIndex + 1);
                }
            }

            throw new InvalidOperationException("Could not extract JSON from response - unbalanced braces.");
        }
    }
}
