using System.Globalization;
using System.Text;

namespace FileMutation.Domain;

public static class MutationPolicy
{
    private const int FixedSuffixByteCount = 12;

    /// <summary>Gets the UTF-8 byte count for a suffix formatted as LF, UTC date, colon, and sequence.</summary>
    public static int GetRequiredByteCount(in MutationContext context) =>
        FixedSuffixByteCount + Encoding.UTF8.GetByteCount(context.RandomSequence);

    /// <summary>Writes a suffix in the stable <c>\nyyyy-MM-dd:sequence</c> format.</summary>
    public static bool TryWriteSuffix(
        Span<byte> destination,
        in MutationContext context,
        out int bytesWritten)
    {
        bytesWritten = 0;
        var requiredByteCount = GetRequiredByteCount(context);
        if (destination.Length < requiredByteCount)
        {
            return false;
        }

        Span<char> date = stackalloc char[10];
        if (!context.UtcNow.TryFormat(date, out var dateCharsWritten, "yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            dateCharsWritten != date.Length)
        {
            return false;
        }

        destination[0] = (byte)'\n';
        Encoding.UTF8.GetBytes(date, destination[1..]);
        destination[11] = (byte)':';
        Encoding.UTF8.GetBytes(context.RandomSequence, destination[FixedSuffixByteCount..]);
        bytesWritten = requiredByteCount;
        return true;
    }
}
