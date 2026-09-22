namespace FileMutation.Domain.Ports;

/// <summary>Generates the character sequence included in a mutation suffix.</summary>
public interface IRandomSequenceGenerator
{
    /// <summary>Fills the supplied span with generated characters.</summary>
    /// <param name="destination">The span whose length determines the sequence length.</param>
    void Fill(Span<char> destination);
}
