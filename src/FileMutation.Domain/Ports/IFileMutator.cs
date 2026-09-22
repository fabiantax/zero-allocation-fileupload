namespace FileMutation.Domain.Ports;

/// <summary>
/// Defines the output transformation required by the file-mutation operation.
/// See <see href="../../../docs/adr/0004-solution-structure-ddd-ports.md">ADR 0004</see>.
/// </summary>
public interface IFileMutator
{
    /// <summary>Copies a source file to a destination and applies the mutation.</summary>
    /// <param name="source">The accepted file content.</param>
    /// <param name="destination">The stream that receives the mutated content.</param>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>A task representing the asynchronous transformation.</returns>
    ValueTask MutateAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default);
}
