using System.Text;
using FileMutation.Application;
using FileMutation.Domain.Ports;
using Xunit;

namespace FileMutation.Application.Tests;

public sealed class SingleFileMutationServiceTests
{
    [Fact]
    public async Task One_caller_can_mutate_three_files_with_independent_results()
    {
        var service = new SingleFileMutationService(new SuffixFileMutator());
        var files = new (Stream Content, string FileName, string ContentType)[]
        {
            new(new MemoryStream("first"u8.ToArray()), "first.txt", "text/plain"),
            new(new MemoryStream("wrong"u8.ToArray()), "wrong.md", "text/plain"),
            new(new MemoryStream([0x61, 0xC3]), "invalid.txt", "text/plain")
        };

        for (var index = 0; index < files.Length; index++)
        {
            await using var result = await service.MutateAsync(
                files[index].Content,
                files[index].FileName,
                files[index].ContentType,
                maxFileBytes: 1024);

            if (result.IsAccepted)
            {
                using var output = new MemoryStream();
                await result.Content.CopyToAsync(output);
                Assert.Equal("first-mutated", Encoding.UTF8.GetString(output.ToArray()));
            }
            else
            {
                Assert.Equal(
                    index == 1 ? FileRejectionReason.UnsupportedFileExtension : FileRejectionReason.InvalidUtf8,
                    result.AcceptanceReason);
            }
        }

        foreach (var file in files)
        {
            await file.Content.DisposeAsync();
        }
    }

    private sealed class SuffixFileMutator : IFileMutator
    {
        public async ValueTask MutateAsync(
            Stream source,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.WriteAsync("-mutated"u8.ToArray(), cancellationToken);
        }
    }
}
