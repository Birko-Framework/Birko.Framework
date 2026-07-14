using System;
using System.IO;

namespace Birko.Helpers
{
    /// <summary>
    /// Provides path validation utilities to prevent path traversal attacks.
    /// </summary>
    public static class PathValidator
    {
        /// <summary>
        /// Validates that a combined path is within the allowed base directory.
        /// </summary>
        /// <param name="basePath">The base directory that should contain the combined path.</param>
        /// <param name="userPath">The user-provided path component to validate.</param>
        /// <param name="combinedPath">The full combined path to validate.</param>
        /// <returns>The validated, normalized full path.</returns>
        /// <exception cref="ArgumentException">Thrown when path traversal is detected.</exception>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null or empty.</exception>
        public static string ValidatePath(string basePath, string userPath, string combinedPath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
            }

            if (string.IsNullOrWhiteSpace(userPath))
            {
                throw new ArgumentException("User path cannot be null or empty.", nameof(userPath));
            }

            if (string.IsNullOrWhiteSpace(combinedPath))
            {
                throw new ArgumentException("Combined path cannot be null or empty.", nameof(combinedPath));
            }

            // Normalize the base path
            var normalizedBasePath = Path.GetFullPath(basePath);

            // Ensure the base path exists or can be created
            if (!Directory.Exists(normalizedBasePath))
            {
                throw new DirectoryNotFoundException($"Base directory does not exist: {normalizedBasePath}");
            }

            // Normalize the combined path
            var normalizedCombinedPath = Path.GetFullPath(combinedPath);

            // Check if the combined path is contained within the base path (prevents directory traversal).
            // Uses boundary-aware containment so a sibling-prefixed path like 'C:\data-evil\x'
            // is not wrongly accepted as 'inside' base 'C:\data' (raw StartsWith would).
            if (!PathHelper.IsUnderDirectory(normalizedCombinedPath, normalizedBasePath))
            {
                throw new ArgumentException(
                    $"Path traversal detected. The combined path '{combinedPath}' attempts to access directories outside the base path '{basePath}'.",
                    nameof(combinedPath)
                );
            }

            return normalizedCombinedPath;
        }

        /// <summary>
        /// Validates and safely combines a base directory with a user-provided path component.
        /// </summary>
        /// <param name="basePath">The base directory (must exist).</param>
        /// <param name="userPath">The user-provided path component (file or directory name).</param>
        /// <returns>The validated, normalized full path.</returns>
        /// <exception cref="ArgumentException">Thrown when path traversal is detected.</exception>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null or empty.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when base directory does not exist.</exception>
        public static string CombineAndValidate(string basePath, string userPath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
            }

            if (string.IsNullOrWhiteSpace(userPath))
            {
                throw new ArgumentException("User path cannot be null or empty.", nameof(userPath));
            }

            // Normalize the base path
            var normalizedBasePath = Path.GetFullPath(basePath);

            // Ensure the base path exists
            if (!Directory.Exists(normalizedBasePath))
            {
                throw new DirectoryNotFoundException($"Base directory does not exist: {normalizedBasePath}");
            }

            // Sanitize user path to remove any potentially dangerous characters
            var sanitizedUserPath = SanitizePath(userPath);

            // Combine paths
            var combinedPath = Path.Combine(normalizedBasePath, sanitizedUserPath);

            // Normalize the combined path
            var normalizedCombinedPath = Path.GetFullPath(combinedPath);

            // Verify the combined path is within the base path (boundary-aware containment,
            // so a sibling-prefixed base can't be spoofed — see PathHelper.IsUnderDirectory).
            if (!PathHelper.IsUnderDirectory(normalizedCombinedPath, normalizedBasePath))
            {
                throw new ArgumentException(
                    $"Path traversal detected. The path '{userPath}' attempts to access directories outside the base path.",
                    nameof(userPath)
                );
            }

            return normalizedCombinedPath;
        }

        /// <summary>
        /// Validates a user-provided path is safe: rejects path traversal (..), absolute paths,
        /// and control characters. Throws <see cref="ArgumentException"/> on violations.
        /// Unlike <see cref="SanitizePath"/>, this method rejects rather than strips dangerous patterns.
        /// </summary>
        /// <param name="path">The user-provided path to validate.</param>
        /// <param name="paramName">Optional parameter name for the exception.</param>
        /// <exception cref="ArgumentException">Thrown when the path contains dangerous patterns.</exception>
        public static void ValidateUserPath(string path, string? paramName = null)
        {
            paramName ??= nameof(path);

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", paramName);
            }

            if (path.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Path traversal detected in: {path}", paramName);
            }

            if (Path.IsPathRooted(path))
            {
                throw new ArgumentException($"Absolute paths are not allowed: {path}", paramName);
            }

            foreach (var c in path)
            {
                if (char.IsControl(c))
                {
                    throw new ArgumentException($"Path contains control characters: {path}", paramName);
                }
            }
        }

        /// <summary>
        /// Normalizes a user-provided path: replaces backslashes with forward slashes and trims leading slashes.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>A normalized path using forward slashes.</returns>
        public static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// Validates a user path and combines it with a base directory, without requiring
        /// the base directory to already exist. Suitable for storage paths where directories
        /// are created on first write.
        /// </summary>
        /// <param name="basePath">The base directory path.</param>
        /// <param name="userPath">The user-provided relative path.</param>
        /// <returns>The validated, normalized full path.</returns>
        /// <exception cref="ArgumentException">Thrown when path traversal is detected.</exception>
        public static string CombineAndValidateUnchecked(string basePath, string userPath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
            }

            if (string.IsNullOrWhiteSpace(userPath))
            {
                throw new ArgumentException("User path cannot be null or empty.", nameof(userPath));
            }

            var normalizedBasePath = Path.GetFullPath(basePath);
            var sanitizedUserPath = SanitizePath(userPath);
            var combinedPath = Path.Combine(normalizedBasePath, sanitizedUserPath);
            var normalizedCombinedPath = Path.GetFullPath(combinedPath);

            // Boundary-aware containment (see PathHelper.IsUnderDirectory) — a raw StartsWith
            // would wrongly accept a sibling-prefixed base such as 'C:\data-evil' under 'C:\data'.
            if (!PathHelper.IsUnderDirectory(normalizedCombinedPath, normalizedBasePath))
            {
                throw new ArgumentException(
                    $"Path traversal detected. The path '{userPath}' attempts to access directories outside the base path.",
                    nameof(userPath));
            }

            return normalizedCombinedPath;
        }

        /// <summary>
        /// Sanitizes a user-provided path component by removing potentially dangerous patterns.
        /// </summary>
        /// <param name="path">The path to sanitize.</param>
        /// <returns>A sanitized path safe for use with the base directory.</returns>
        public static string SanitizePath(string path)
        {
            // Remove any null characters
            path = path.Replace("\0", string.Empty);

            // Remove path-traversal tokens repeatedly until the string stops changing. CR-M195: a single
            // non-recursive pass let overlapping/nested sequences re-form a token — e.g. "..././" loses
            // the inner "./" and becomes "../", and "....//"-style inputs can re-create "../" after one
            // removal. Looping to a fixpoint collapses every such reconstruction.
            string previous;
            do
            {
                previous = path;
                path = path.Replace("../", string.Empty)
                           .Replace("..\\", string.Empty)
                           .Replace("./", string.Empty)
                           .Replace(".\\", string.Empty);
            }
            while (path != previous);

            // Remove leading slashes or backslashes (prevents absolute path injection)
            path = path.TrimStart('/', '\\');

            // Remove any drive letter specifications (prevents Windows drive traversal)
            if (path.Length >= 2 && path[1] == ':')
            {
                path = path.Substring(2);
            }

            return path;
        }

        /// <summary>
        /// Validates that a directory path is safe to use.
        /// </summary>
        /// <param name="directoryPath">The directory path to validate.</param>
        /// <returns>The validated, normalized directory path.</returns>
        /// <exception cref="ArgumentException">Thrown when the path contains invalid characters.</exception>
        public static string ValidateDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Directory path cannot be null or empty.", nameof(directoryPath));
            }

            // Validate against path-invalid characters ONLY. Path.GetInvalidFileNameChars() additionally
            // includes the directory separators ('\', '/') and the drive-letter ':' on Windows, so checking
            // the whole directory path against it rejected every absolute Windows path (e.g. "C:\Users\...").
            // A directory path legitimately contains separators and a drive colon; only genuinely-invalid
            // path characters (control chars, etc.) should be rejected here.
            if (directoryPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException($"Directory path contains invalid characters: {directoryPath}", nameof(directoryPath));
            }

            // Normalize and return the path
            return Path.GetFullPath(directoryPath);
        }
    }
}
