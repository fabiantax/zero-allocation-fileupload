namespace FileMutation.Domain;

public readonly ref struct MutationContext
{
    public MutationContext(DateTimeOffset utcNow, ReadOnlySpan<char> randomSequence)
    {
        UtcNow = utcNow.ToUniversalTime();
        RandomSequence = randomSequence;
    }

    public DateTimeOffset UtcNow { get; }

    public ReadOnlySpan<char> RandomSequence { get; }
}
