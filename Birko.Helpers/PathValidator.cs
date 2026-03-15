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

            // Check if the combined path starts with the base path (prevents directory traversal)
            if (!normalizedCombinedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
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

            // Verify the combined path is within the base path
            if (!normalizedCombinedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
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

            if (!normalizedCombinedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
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

            // Remove any attempts at path traversal with forward slashes
            path = path.Replace("../", string.Empty)
                       .Replace("..\\", string.Empty)
                       .Replace("./", string.Empty)
                       .Replace(".\\", string.Empty);

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

            // Get invalid characters for file and directory names
            var invalidChars = Path.GetInvalidPathChars();
            var invalidFileNameChars = Path.GetInvalidFileNameChars();

            // Combine and check for invalid characters
            var allInvalidChars = new char[invalidChars.Length + invalidFileNameChars.Length];
            Array.Copy(invalidChars, allInvalidChars, invalidChars.Length);
            Array.Copy(invalidFileNameChars, 0, allInvalidChars, invalidChars.Length, invalidFileNameChars.Length);

            // Check if the path contains invalid characters
            if (directoryPath.IndexOfAny(allInvalidChars) >= 0)
            {
                throw new ArgumentException($"Directory path contains invalid characters: {directoryPath}", nameof(directoryPath));
            }

            // Normalize and return the path
            return Path.GetFullPath(directoryPath);
        }
    }
}
