namespace Lockjaw.Core;

public static class LockjawConstants
{
    public const string ProductName = "Lockjaw";
    public const string BinaryExtension = ".lockjaw";
    public const string ArmoredExtension = ".lockjaw.txt";

    public const ushort FormatVersion = 1;
    public const byte PassphraseMode = 1;
    public const byte Argon2Id13Kdf = 1;

    public const int PreambleLength = 11;
    public const int PassphraseHeaderLength = 29;
    public const int SaltLength = 16;
    public const int PlaintextChunkSize = 1024 * 1024;
    public const int MaximumHeaderLength = 1024 * 1024;
    public const int MaximumKdfMemoryBytes = 1024 * 1024 * 1024;
    public const int MinimumKdfMemoryBytes = 8 * 1024 * 1024;
    public const int MaximumKdfIterations = 10;

    public const byte PayloadUncompressedTar = 0;
    public const byte PayloadDeflateTar = 1;

    public const string ArmorBegin = "-----BEGIN LOCKJAW ENCRYPTED FILE-----";
    public const string ArmorEnd = "-----END LOCKJAW ENCRYPTED FILE-----";

    internal static ReadOnlySpan<byte> Magic => "LKJW"u8;
}

