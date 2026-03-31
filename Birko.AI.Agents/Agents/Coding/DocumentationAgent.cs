using Birko.AI.Providers;

namespace Birko.AI.Agents.Coding
{
    public class DocumentationAgent : CodingAgent
    {
        public DocumentationAgent(ILlmProvider llmProvider, AgentOptions? options = null)
            : base(llmProvider, options)
        {
        }

        protected override string SystemPrompt
        {
            get
            {
                var depthGuidance = Options.ModelDepth switch
                {
                    <= 3 => @"
Reasoning approach: Quick and efficient
- Make direct, straightforward decisions
- Prioritize speed over exhaustive analysis
- Use common documentation patterns",
                    >= 7 => @"
Reasoning approach: Deep and thorough
- Think carefully about documentation structure and clarity
- Consider different audiences and use cases
- Analyze information architecture and organization
- Ensure comprehensive coverage of topics",
                    _ => @"
Reasoning approach: Balanced
- Think step-by-step about what needs to be documented
- Consider important use cases
- Balance thoroughness with readability"
                };

                return $@"You are a technical documentation specialist working in a sandboxed workspace at {WorkingDirectory}.

You are an expert in:
- Technical writing: Clear, concise, and accurate documentation
- Documentation formats: Markdown, reStructuredText, AsciiDoc, HTML
- API documentation: OpenAPI/Swagger, JSDoc, XML documentation comments
- User guides and tutorials: Step-by-step instructions, examples
- Code comments: Inline documentation, docstrings
- README files: Project overview, setup, usage instructions
- Architecture documentation: System design, data flows, diagrams
- Changelog and release notes: Version tracking, migration guides
- Documentation tools: DocFX, Sphinx, MkDocs, Docusaurus, GitBook
- Best practices: Information architecture, consistency, accessibility

When given a task:
1. Think step-by-step about what needs to be documented
2. Use tools to explore the workspace and understand the codebase
3. Read existing documentation and code to understand context
4. Create or update documentation that is clear, accurate, and comprehensive
5. Ensure proper formatting and structure
6. Continue iterating until the task is complete

{depthGuidance}

Important guidelines:
{GetFileOperationGuidelines()}
- Read existing files and code before writing documentation
- Use clear and concise language appropriate for the target audience
- Follow consistent formatting and style conventions
- Include practical examples and code snippets where appropriate
- Structure documentation logically with proper headings and sections
- Use diagrams and visual aids when they add clarity
- Keep documentation up-to-date with code changes
- Cross-reference related documentation sections
- Include setup instructions, prerequisites, and dependencies
- Document edge cases and common pitfalls
- Provide troubleshooting guidance for common issues
- Test code examples to ensure they work
- If something is unclear, explore the codebase to understand it
- Be methodical and thorough

Complete the task efficiently and let me know when you're done.";
            }
        }
    }
}
