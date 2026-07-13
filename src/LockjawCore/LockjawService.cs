using System.Buffers;
using System.IO.Compression;
using LibSodium;

namespace Lockjaw.Core;

public static class LockjawService
{
    public static void Encrypt(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        ReadOnlySpan<char> passphrase,
        LockjawEncryptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (passphrase.IsEmpty)
        {
            throw new ArgumentException("The passphrase cannot be empty.", nameof(passphrase));
        }

        options ??= new LockjawEncryptionOptions();
        string[] normalizedInputs = NormalizeInputs(inputPaths);
        string destination = Path.GetFullPath(outputPath);
        ValidateOutputPath(destination, normalizedInputs);

        string parent = Path.GetDirectoryName(destination) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(parent);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException($"Output already exists: {destination}");
        }

        string temporaryOutput = CreateUnusedPath(parent, ".lockjaw-output", ".tmp");
        try
        {
            using (var output = new FileStream(
                temporaryOutput,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.SequentialScan))
            {
                if (options.Armor)
                {
                    using FileStream binary = CreateDeleteOnCloseFile(parent, ".lockjaw-binary");
                    EncryptBinary(normalizedInputs, binary, passphrase, options, cancellationToken);
                    binary.Flush(flushToDisk: true);
                    binary.Position = 0;
                    ArmorCodec.Encode(binary, output, cancellationToken);
                }
                else
                {
                    EncryptBinary(normalizedInputs, output, passphrase, options, cancellationToken);
                }

                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryOutput, destination);
        }
        catch
        {
            TryDeleteFile(temporaryOutput);
            throw;
        }
    }

    public static void Decrypt(
        string inputPath,
        string outputDirectory,
        ReadOnlySpan<char> passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (passphrase.IsEmpty)
        {
            throw new ArgumentException("The passphrase cannot be empty.", nameof(passphrase));
        }

        string sourcePath = Path.GetFullPath(inputPath);
        string destination = Path.GetFullPath(outputDirectory);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Encrypted input file was not found.", sourcePath);
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException($"Output directory already exists: {destination}");
        }

        string parent = Path.GetDirectoryName(destination) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(parent);

        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);

        bool armored = ArmorCodec.IsArmored(source);
        using FileStream? decoded = armored
            ? CreateDeleteOnCloseFile(parent, ".lockjaw-decoded")
            : null;
        Stream binaryInput = source;
        if (decoded is not null)
        {
            ArmorCodec.Decode(source, decoded, cancellationToken);
            decoded.Position = 0;
            binaryInput = decoded;
        }

        using FileStream packedPayload = CreateDeleteOnCloseFile(parent, ".lockjaw-payload");
        DecryptBinaryToPackedPayload(binaryInput, packedPayload, passphrase, cancellationToken);
        packedPayload.Flush(flushToDisk: true);
        packedPayload.Position = 0;

        int payloadKind = packedPayload.ReadByte();
        if (payloadKind is not LockjawConstants.PayloadUncompressedTar and not LockjawConstants.PayloadDeflateTar)
        {
            throw new LockjawFormatException("The encrypted payload type is invalid.");
        }

        string stagingDirectory = CreateUnusedPath(parent, ".lockjaw-stage", string.Empty);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            if (payloadKind == LockjawConstants.PayloadDeflateTar)
            {
                using var decompressed = new DeflateStream(
                    packedPayload,
                    CompressionMode.Decompress,
                    leaveOpen: true);
                ArchiveCodec.Extract(decompressed, stagingDirectory, cancellationToken);
            }
            else
            {
                ArchiveCodec.Extract(packedPayload, stagingDirectory, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, destination);
        }
        catch (Exception original)
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (Exception cleanup)
            {
                throw new IOException(
                    $"Decryption failed and the staging directory could not be removed: {stagingDirectory}",
                    new AggregateException(original, cleanup));
            }

            throw;
        }
    }

    public static LockjawInspection Inspect(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        string sourcePath = Path.GetFullPath(inputPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Encrypted input file was not found.", sourcePath);
        }

        string temporaryDirectory = Path.GetTempPath();
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);

        bool armored = ArmorCodec.IsArmored(source);
        using FileStream? decoded = armored
            ? CreateDeleteOnCloseFile(temporaryDirectory, ".lockjaw-inspect")
            : null;
        Stream binaryInput = source;
        if (decoded is not null)
        {
            ArmorCodec.Decode(source, decoded, cancellationToken);
            decoded.Position = 0;
            binaryInput = decoded;
        }

        ParsedLockjawHeader parsed = LockjawHeader.Read(binaryInput);
        return new LockjawInspection(
            parsed.Header.Version,
            "passphrase",
            "Argon2id13",
            parsed.Header.KdfIterations,
            parsed.Header.KdfMemoryBytes,
            parsed.Header.PlaintextChunkSize,
            armored);
    }

    private static void EncryptBinary(
        IReadOnlyList<string> inputPaths,
        Stream output,
        ReadOnlySpan<char> passphrase,
        LockjawEncryptionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LockjawHeader header = LockjawHeader.Create(options);
        using SecureMemory<byte> key = PassphraseKey.Derive(passphrase, header);
        using var state = new SecureMemory<byte>(CryptoSecretStream.StateLen);
        byte[] secretStreamHeader = new byte[CryptoSecretStream.HeaderLen];
        CryptoSecretStream.InitializeEncryption(state, secretStreamHeader, key);

        byte[] authenticatedData = header.SerializeWithSecretStreamHeader(secretStreamHeader);
        output.Write(authenticatedData);

        using var encrypted = new SecretStreamEncryptingWriteStream(
            output,
            state,
            authenticatedData,
            LockjawConstants.PlaintextChunkSize);

        encrypted.WriteByte(options.Compress
            ? LockjawConstants.PayloadDeflateTar
            : LockjawConstants.PayloadUncompressedTar);

        if (options.Compress)
        {
            using var compressed = new DeflateStream(
                encrypted,
                CompressionLevel.Optimal,
                leaveOpen: true);
            ArchiveCodec.Write(inputPaths, compressed, cancellationToken);
        }
        else
        {
            ArchiveCodec.Write(inputPaths, encrypted, cancellationToken);
        }

        encrypted.Complete();
    }

    private static void DecryptBinaryToPackedPayload(
        Stream input,
        Stream packedOutput,
        ReadOnlySpan<char> passphrase,
        CancellationToken cancellationToken)
    {
        ParsedLockjawHeader parsed = LockjawHeader.Read(input);
        using SecureMemory<byte> key = PassphraseKey.Derive(passphrase, parsed.Header);
        using var state = new SecureMemory<byte>(CryptoSecretStream.StateLen);

        try
        {
            CryptoSecretStream.InitializeDecryption(state, parsed.SecretStreamHeader, key);
            int encryptedChunkSize = parsed.Header.PlaintextChunkSize + CryptoSecretStream.OverheadLen;
            byte[] ciphertext = ArrayPool<byte>.Shared.Rent(encryptedChunkSize);
            byte[] plaintext = ArrayPool<byte>.Shared.Rent(parsed.Header.PlaintextChunkSize);
            try
            {
                bool finalSeen = false;
                while (!finalSeen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = StreamHelpers.ReadUpTo(input, ciphertext.AsSpan(0, encryptedChunkSize));
                    if (count < CryptoSecretStream.OverheadLen)
                    {
                        throw new LockjawAuthenticationException();
                    }

                    Span<byte> cleartext = CryptoSecretStream.DecryptChunk(
                        state,
                        plaintext,
                        out CryptoSecretStreamTag tag,
                        ciphertext.AsSpan(0, count),
                        parsed.AuthenticatedData);
                    packedOutput.Write(cleartext);
                    cleartext.Clear();

                    switch (tag)
                    {
                        case CryptoSecretStreamTag.Message:
                            if (count != encryptedChunkSize)
                            {
                                throw new LockjawAuthenticationException();
                            }

                            break;

                        case CryptoSecretStreamTag.Final:
                            if (input.ReadByte() != -1)
                            {
                                throw new LockjawAuthenticationException();
                            }

                            finalSeen = true;
                            break;

                        default:
                            throw new LockjawAuthenticationException();
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(ciphertext, clearArray: true);
                ArrayPool<byte>.Shared.Return(plaintext, clearArray: true);
            }
        }
        catch (LockjawAuthenticationException)
        {
            throw;
        }
        catch (LibSodiumException exception)
        {
            throw new LockjawAuthenticationException(exception);
        }
    }

    private static string[] NormalizeInputs(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input path is required.", nameof(inputPaths));
        }

        string[] normalized = new string[inputPaths.Count];
        for (int index = 0; index < inputPaths.Count; index++)
        {
            string path = inputPaths[index];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Input paths cannot be empty.", nameof(inputPaths));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                throw new FileNotFoundException("Input path was not found.", fullPath);
            }

            normalized[index] = fullPath;
        }

        return normalized;
    }

    private static void ValidateOutputPath(string outputPath, IReadOnlyList<string> inputPaths)
    {
        foreach (string inputPath in inputPaths)
        {
            if (File.Exists(inputPath) && PathsEqual(inputPath, outputPath))
            {
                throw new IOException("The output path cannot replace an input file.");
            }

            if (Directory.Exists(inputPath))
            {
                string directoryPrefix = Path.TrimEndingDirectorySeparator(inputPath) + Path.DirectorySeparatorChar;
                if (outputPath.StartsWith(directoryPrefix, PathComparison))
                {
                    throw new IOException("The output file cannot be placed inside a folder being encrypted.");
                }
            }
        }
    }

    private static FileStream CreateDeleteOnCloseFile(string directory, string prefix)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string path = CreateUnusedPath(directory, prefix, ".tmp");
            try
            {
                return new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 19)
            {
            }
        }

        throw new IOException("Unable to create a secure temporary file.");
    }

    private static string CreateUnusedPath(string directory, string prefix, string suffix)
    {
        return Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}{suffix}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // This file contains ciphertext only. Preserve the original error.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

