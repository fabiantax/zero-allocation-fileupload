using System.Security.Cryptography;

namespace FileMutation.Domain.Batches;

/// <summary>Identifies one batch upload: 128 random bits as 32 lowercase hex characters.</summary>
public readonly record struct BatchId
{
    private const int HexLength = 32;

    private BatchId(string value) => Value = value;

    /// <summary>Gets the 32-character lowercase hex value.</summary>
    public string Value { get; }

    /// <summary>Creates an identifier from a cryptographic random source.</summary>
    /// <returns>A new identifier.</returns>
    public static BatchId New() => new(RandomNumberGenerator.GetHexString(HexLength, lowercase: true));

    /// <summary>Parses exactly 32 characters of [0-9a-f]; anything else fails.</summary>
    /// <param name="value">The candidate text.</param>
    /// <param name="id">The parsed identifier when parsing succeeds; otherwise the default value.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid identifier.</returns>
    public static bool TryParse(string? value, out BatchId id)
    {
        if (value is { Length: HexLength } && value.All(IsLowerHex))
        {
            id = new BatchId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The 32-character lowercase hex value.</returns>
    public override string ToString() => Value;

    private static bool IsLowerHex(char character) => character is (>= '0' and <= '9') or (>= 'a' and <= 'f');
}
