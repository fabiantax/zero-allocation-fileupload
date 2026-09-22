using System.Diagnostics.CodeAnalysis;

namespace FileMutation.Domain;

public sealed record FileName
{
    public FileName(string value)
    {
        if (!TrySanitize(value, out var sanitized))
        {
            throw new ArgumentException("A file name must be non-empty and contain no path or control characters.", nameof(value));
        }

        Value = sanitized;
    }

    public string Value { get; }

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
