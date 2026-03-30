using XenoAtom.Terminal.UI.Controls;

namespace Birko.Framework.Examples
{
    /// <summary>
    /// Provides rich TUI output for examples. Falls back to Console when no LogControl is set.
    /// Use this instead of Console.WriteLine for colored/formatted output in examples.
    /// </summary>
    public static class ExampleOutput
    {
        private static LogControl? _log;

        /// <summary>
        /// Sets the LogControl target for TUI output.
        /// </summary>
        public static void SetTarget(LogControl log) => _log = log;

        /// <summary>
        /// Clears the TUI target (reverts to Console output).
        /// </summary>
        public static void ClearTarget() => _log = null;

        /// <summary>
        /// Writes a plain text line.
        /// </summary>
        public static void WriteLine(string text = "")
        {
            if (_log != null)
            {
                Dispatch(() =>
                {
                    _log.AppendLine(text);
                    _log.ScrollToTail();
                });
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        /// <summary>
        /// Writes a line with markup formatting (e.g., "[bold cyan]title[/]").
        /// Falls back to plain text on Console.
        /// </summary>
        public static void WriteMarkupLine(string markup)
        {
            if (_log != null)
            {
                Dispatch(() =>
                {
                    _log.AppendMarkupLine(markup);
                    _log.ScrollToTail();
                });
            }
            else
            {
                Console.WriteLine(StripMarkup(markup));
            }
        }

        /// <summary>
        /// Dispatches an action to the LogControl's UI thread.
        /// If already on the UI thread, executes immediately.
        /// </summary>
        private static void Dispatch(Action action)
        {
            if (_log!.CheckAccess())
            {
                action();
            }
            else
            {
                _log.Dispatcher.Post(action);
            }
        }

        /// <summary>
        /// Writes a section header.
        /// </summary>
        public static void WriteHeader(string title)
        {
            WriteMarkupLine($"[bold cyan]  {title}[/]");
            WriteLine(new string('─', 40));
        }

        /// <summary>
        /// Writes a success line.
        /// </summary>
        public static void WriteSuccess(string text) =>
            WriteMarkupLine($"[green]  ✓[/] {EscapeMarkup(text)}");

        /// <summary>
        /// Writes a warning line.
        /// </summary>
        public static void WriteWarning(string text) =>
            WriteMarkupLine($"[yellow]  ⚠[/] {EscapeMarkup(text)}");

        /// <summary>
        /// Writes an error line.
        /// </summary>
        public static void WriteError(string text) =>
            WriteMarkupLine($"[red]  ✗[/] {EscapeMarkup(text)}");

        /// <summary>
        /// Writes a key-value info line.
        /// </summary>
        public static void WriteInfo(string key, string value) =>
            WriteMarkupLine($"  [dim]{EscapeMarkup(key)}:[/] {EscapeMarkup(value)}");

        /// <summary>
        /// Writes a dimmed/muted line.
        /// </summary>
        public static void WriteDim(string text) =>
            WriteMarkupLine($"[dim]  {EscapeMarkup(text)}[/]");

        private static string EscapeMarkup(string text) =>
            text.Replace("[", "[[").Replace("]", "]]");

        private static string StripMarkup(string markup)
        {
            // Simple strip: remove [tag] and [/tag] patterns
            var result = System.Text.RegularExpressions.Regex.Replace(markup, @"\[/?[^\]]*\]", "");
            return result;
        }
    }
}
