namespace FileMutation.Domain.Ports;

public interface IRandomSequenceGenerator
{
    void Fill(Span<char> destination);
}
