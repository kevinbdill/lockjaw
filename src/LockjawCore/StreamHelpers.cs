using System.Buffers;

namespace Lockjaw.Core;

internal static class StreamHelpers
{
    public static void ReadExactly(Stream input, Span<byte> destination, string description)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = input.Read(destination[read..]);
            if (count == 0)
            {
                throw new LockjawFormatException($"The Lockjaw file ended while reading {description}.");
            }

            read += count;
        }
    }

    public static int ReadUpTo(Stream input, Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = input.Read(destination[read..]);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return read;
    }

    public static void CopyTo(Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}

