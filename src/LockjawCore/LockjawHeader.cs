using System.Buffers.Binary;
using LibSodium;

namespace Lockjaw.Core;

internal sealed record LockjawHeader(
    ushort Version,
    byte Mode,
    byte Kdf,
    int KdfIterations,
    int KdfMemoryBytes,
    byte[] Salt,
    int PlaintextChunkSize)
{
    public static LockjawHeader Create(LockjawEncryptionOptions options)
    {
        (int iterations, int memoryBytes) = options.ResolveKdfParameters();
        byte[] salt = new byte[LockjawConstants.SaltLength];
        RandomGenerator.Fill(salt);

        return new LockjawHeader(
            LockjawConstants.FormatVersion,
            LockjawConstants.PassphraseMode,
            LockjawConstants.Argon2Id13Kdf,
            iterations,
            memoryBytes,
            salt,
            LockjawConstants.PlaintextChunkSize);
    }

    public byte[] SerializeWithSecretStreamHeader(ReadOnlySpan<byte> secretStreamHeader)
    {
        if (secretStreamHeader.Length != CryptoSecretStream.HeaderLen)
        {
            throw new ArgumentException("Invalid secretstream header length.", nameof(secretStreamHeader));
        }

        byte[] result = new byte[
            LockjawConstants.PreambleLength +
            LockjawConstants.PassphraseHeaderLength +
            CryptoSecretStream.HeaderLen];

        Span<byte> span = result;
        LockjawConstants.Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], Version);
        span[6] = Mode;
        BinaryPrimitives.WriteUInt32LittleEndian(
            span[7..11],
            LockjawConstants.PassphraseHeaderLength);

        int offset = LockjawConstants.PreambleLength;
        span[offset] = Kdf;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 1)..(offset + 5)], checked((uint)KdfIterations));
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 5)..(offset + 9)], checked((uint)KdfMemoryBytes));
        Salt.CopyTo(span[(offset + 9)..(offset + 25)]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 25)..(offset + 29)], checked((uint)PlaintextChunkSize));
        secretStreamHeader.CopyTo(span[(offset + LockjawConstants.PassphraseHeaderLength)..]);
        return result;
    }

    public static ParsedLockjawHeader Read(Stream input)
    {
        Span<byte> preamble = stackalloc byte[LockjawConstants.PreambleLength];
        StreamHelpers.ReadExactly(input, preamble, "the public header");

        if (!preamble[..4].SequenceEqual(LockjawConstants.Magic))
        {
            throw new LockjawFormatException("This is not a Lockjaw encrypted file.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(preamble[4..6]);
        if (version != LockjawConstants.FormatVersion)
        {
            throw new LockjawFormatException($"Lockjaw format version {version} is not supported.");
        }

        byte mode = preamble[6];
        if (mode != LockjawConstants.PassphraseMode)
        {
            throw new LockjawFormatException("This build supports passphrase-mode Lockjaw files only.");
        }

        uint headerLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(preamble[7..11]);
        if (headerLengthValue is 0 or > LockjawConstants.MaximumHeaderLength)
        {
            throw new LockjawFormatException("The Lockjaw header length is invalid.");
        }

        int headerLength = checked((int)headerLengthValue);
        if (headerLength != LockjawConstants.PassphraseHeaderLength)
        {
            throw new LockjawFormatException("The passphrase header length is invalid for format version 1.");
        }

        byte[] modeHeader = new byte[headerLength];
        StreamHelpers.ReadExactly(input, modeHeader, "the passphrase header");

        byte kdf = modeHeader[0];
        if (kdf != LockjawConstants.Argon2Id13Kdf)
        {
            throw new LockjawFormatException("The file uses an unsupported password derivation algorithm.");
        }

        uint iterationsValue = BinaryPrimitives.ReadUInt32LittleEndian(modeHeader.AsSpan(1, 4));
        uint memoryValue = BinaryPrimitives.ReadUInt32LittleEndian(modeHeader.AsSpan(5, 4));
        uint chunkValue = BinaryPrimitives.ReadUInt32LittleEndian(modeHeader.AsSpan(25, 4));

        if (iterationsValue is 0 or > LockjawConstants.MaximumKdfIterations)
        {
            throw new LockjawFormatException("The Argon2id operation count is outside the supported safety limits.");
        }

        if (memoryValue is < LockjawConstants.MinimumKdfMemoryBytes or > LockjawConstants.MaximumKdfMemoryBytes)
        {
            throw new LockjawFormatException("The Argon2id memory request is outside the supported safety limits.");
        }

        if (chunkValue != LockjawConstants.PlaintextChunkSize)
        {
            throw new LockjawFormatException("The secretstream chunk size is invalid for format version 1.");
        }

        byte[] salt = modeHeader.AsSpan(9, LockjawConstants.SaltLength).ToArray();
        byte[] secretStreamHeader = new byte[CryptoSecretStream.HeaderLen];
        StreamHelpers.ReadExactly(input, secretStreamHeader, "the secretstream header");

        var header = new LockjawHeader(
            version,
            mode,
            kdf,
            checked((int)iterationsValue),
            checked((int)memoryValue),
            salt,
            checked((int)chunkValue));

        byte[] authenticatedData = new byte[
            preamble.Length + modeHeader.Length + secretStreamHeader.Length];
        preamble.CopyTo(authenticatedData);
        modeHeader.CopyTo(authenticatedData, preamble.Length);
        secretStreamHeader.CopyTo(authenticatedData, preamble.Length + modeHeader.Length);

        return new ParsedLockjawHeader(header, secretStreamHeader, authenticatedData);
    }
}

internal sealed record ParsedLockjawHeader(
    LockjawHeader Header,
    byte[] SecretStreamHeader,
    byte[] AuthenticatedData);

