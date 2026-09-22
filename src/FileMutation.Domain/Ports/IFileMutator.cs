namespace FileMutation.Domain.Ports;

public interface IFileMutator
{
    ValueTask MutateAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default);
}
