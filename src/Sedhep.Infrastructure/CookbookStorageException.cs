// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Infrastructure;

/// <summary>
/// Raised when the cookbook folder exists but this process is not allowed to write to it. The app
/// shows the message to the user instead of crashing: a read-only location is a situation to
/// explain, not a stack trace to dump.
/// </summary>
public sealed class CookbookStorageException : Exception
{
    public CookbookStorageException(string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
    }

    /// <summary>The database or folder that could not be written to.</summary>
    public string Path { get; }
}
