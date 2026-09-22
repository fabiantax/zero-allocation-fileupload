using System.Security.Cryptography;
using FileMutation.Domain.Ports;

namespace FileMutation.Infrastructure;

/// <summary>
/// Fills a sequence with cryptographically strong characters drawn from an unambiguous alphabet.
/// </summary>
internal sealed class CryptoRandomSequenceGenerator : IRandomSequenceGenerator
{
    // Excludes 0/O and 1/I/l so a sequence survives being read aloud or retyped.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Fills <paramref name="destination"/> with random characters from the alphabet.</summary>
    public void Fill(Span<char> destination) => RandomNumberGenerator.GetItems<char>(Alphabet, destination);
}
