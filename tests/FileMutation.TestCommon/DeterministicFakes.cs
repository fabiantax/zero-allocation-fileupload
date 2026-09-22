using FileMutation.Domain.Ports;

namespace FileMutation.TestCommon;

public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

public sealed class FixedRandomSequenceGenerator(string sequence) : IRandomSequenceGenerator
{
    public void Fill(Span<char> destination)
    {
        if (destination.Length != sequence.Length)
        {
            throw new ArgumentException("The requested sequence length does not match the fixed sequence.", nameof(destination));
        }

        sequence.AsSpan().CopyTo(destination);
    }
}
