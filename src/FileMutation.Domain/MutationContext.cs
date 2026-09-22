namespace FileMutation.Domain;

/// <summary>Supplies the deterministic values used to format one mutation suffix.</summary>
public readonly ref struct MutationContext
{
    /// <summary>Creates a mutation context and normalizes its timestamp to UTC.</summary>
    /// <param name="utcNow">The timestamp whose UTC date is appended.</param>
    /// <param name="randomSequence">The characters appended after the date.</param>
    public MutationContext(DateTimeOffset utcNow, ReadOnlySpan<char> randomSequence)
    {
        UtcNow = utcNow.ToUniversalTime();
        RandomSequence = randomSequence;
    }

    /// <summary>Gets the timestamp normalized to UTC.</summary>
    public DateTimeOffset UtcNow { get; }

    /// <summary>Gets the random character sequence appended to the file.</summary>
    public ReadOnlySpan<char> RandomSequence { get; }
}
