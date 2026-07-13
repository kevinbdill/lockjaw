using System.Buffers;
using System.Text;

namespace Lockjaw.Core;

internal static class ArmorCodec
{
    public static bool IsArmored(Stream input)
    {
        if (!input.CanSeek)
        {
            throw new ArgumentException("Armor detection requires a seekable stream.", nameof(input));
        }

        long position = input.Position;
        try
        {
            Span<byte> prefix = stackalloc byte[LockjawConstants.ArmorBegin.Length];
            int read = StreamHelpers.ReadUpTo(input, prefix);
            return read == prefix.Length &&
                prefix.SequenceEqual(Encoding.ASCII.GetBytes(LockjawConstants.ArmorBegin));
        }
        finally
        {
            input.Position = position;
        }
    }

    public static void Encode(Stream binaryInput, Stream armoredOutput, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(
            armoredOutput,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            NewLine = "\n",
        };

        writer.WriteLine(LockjawConstants.ArmorBegin);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(48);
        try
        {
            int read;
            while ((read = StreamHelpers.ReadUpTo(binaryInput, buffer.AsSpan(0, 48))) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(Convert.ToBase64String(buffer, 0, read));
            }

            writer.WriteLine(LockjawConstants.ArmorEnd);
            writer.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public static void Decode(Stream armoredInput, Stream binaryOutput, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            armoredInput,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        string? first = reader.ReadLine();
        if (!string.Equals(first, LockjawConstants.ArmorBegin, StringComparison.Ordinal))
        {
            throw new LockjawFormatException("The Lockjaw armor header is missing or invalid.");
        }

        bool foundFooter = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(line, LockjawConstants.ArmorEnd, StringComparison.Ordinal))
            {
                foundFooter = true;
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length > 256)
            {
                throw new LockjawFormatException("A Lockjaw armor line is too long.");
            }

            try
            {
                byte[] decoded = Convert.FromBase64String(line);
                binaryOutput.Write(decoded);
                decoded.AsSpan().Clear();
            }
            catch (FormatException exception)
            {
                throw new LockjawFormatException("The Lockjaw armor contains invalid Base64 text.", exception);
            }
        }

        if (!foundFooter)
        {
            throw new LockjawFormatException("The Lockjaw armor footer is missing.");
        }

        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                throw new LockjawFormatException("Unexpected text follows the Lockjaw armor footer.");
            }
        }

        binaryOutput.Flush();
    }
}
