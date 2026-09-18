using Birko.AI.Providers;

namespace Birko.AI.Agents
{
    public class CodingAgent : Agent
    {
        public CodingAgent(ILlmProvider llmProvider, AgentOptions? options = null)
            : base(llmProvider, options)
        {
        }

        protected override string SystemPrompt
        {
            get
            {
                return $@"You are a helpful coding assistant working in a sandboxed workspace at {WorkingDirectory}.

You have access to tools that let you read, write, and execute code. When given a task:
1. Think step-by-step about what you need to do
2. Use tools to explore the workspace, read files, make changes
3. Test your changes by running code
4. Continue iterating until the task is complete

{GetDepthGuidance()}

Important guidelines:
{GetFileOperationGuidelines()}
{GetCommonBestPractices()}

Complete the task efficiently and let me know when you're done.";
            }
        }
    }
}
