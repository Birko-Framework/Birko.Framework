namespace Birko.AI.Tools
{
    /// <summary>
    /// How a file tool turns a model-supplied path into a full path (TASK-173, SH-L171): the one producer, so every file
    /// tool reads <c>/src/x.cs</c> the same way. A single leading '/' means "relative to the workspace" (a model's habit);
    /// <c>//</c> and drive-rooted paths stay absolute and must then pass the allow-list.
    /// </summary>
    internal static class WorkspacePath
    {
        /// <returns>The normalized workspace root and the full path of <paramref name="path"/> within (or outside) it.</returns>
        public static (string Root, string FullPath) Resolve(string workingDirectory, string path)
        {
            var root = Path.GetFullPath(workingDirectory);
            var relative = path.StartsWith('/') && !path.StartsWith("//") ? path[1..] : path;
            return (root, Path.GetFullPath(Path.Combine(root, relative)));
        }
    }
}
