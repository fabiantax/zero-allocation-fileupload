namespace FileMutation.Application;

public interface IMutateFileUseCase
{
    ValueTask ExecuteAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default);
}
