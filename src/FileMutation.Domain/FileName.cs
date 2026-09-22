using System.Diagnostics.CodeAnalysis;

namespace FileMutation.Domain;

/// <summary>
/// Represents a safe download filename without path or control characters.
/// See <see href="../../docs/adr/0004-solution-structure-ddd-ports.md">ADR 0004</see>.
/// </summary>
public sealed record FileName
{
    /// <summary>Creates a filename from a value that can be used safely as a download name.</summary>
    /// <param name="value">The submitted filename.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not safe.</exception>
    public FileName(string value)
    {
        if (!TrySanitize(value, out var sanitized))
        {
            throw new ArgumentException("A file name must be non-empty and contain no path or control characters.", nameof(value));
        }

        Value = sanitized;
    }

    /// <summary>Gets the validated and trimmed filename.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a safe filename without throwing for invalid input.</summary>
    /// <param name="value">The submitted filename.</param>
    /// <param name="fileName">The safe filename when validation succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value is a safe filename.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out FileName? fileName)
    {
        if (!TrySanitize(value, out var sanitized))
        {
            fileName = null;
            return false;
        }

        fileName = new FileName(sanitized);
        return true;
    }

    /// <summary>Returns the validated filename value.</summary>
    /// <returns>The value used as the download filename.</returns>
    public override string ToString() => Value;

    private static bool TrySanitize(string? value, out string sanitized)
    {
        sanitized = value?.Trim() ?? string.Empty;
        if (sanitized.Length == 0 || sanitized is "." or "..")
        {
            return false;
        }

        foreach (var character in sanitized)
        {
            if (character is '/' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
