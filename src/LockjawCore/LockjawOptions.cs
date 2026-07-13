namespace Lockjaw.Core;

public sealed record LockjawEncryptionOptions
{
    public bool Compress { get; init; } = true;

    public bool Armor { get; init; }

    /// <summary>
    /// Overrides the v1 Argon2id operation count. Leave null for the production
    /// default (3, or 4 on a constrained machine).
    /// </summary>
    public int? KdfIterations { get; init; }

    /// <summary>
    /// Overrides Argon2id memory in bytes. Leave null for the production
    /// default (256 MiB, or 64 MiB on a constrained machine).
    /// Values below 64 MiB are intended only for automated tests.
    /// </summary>
    public int? KdfMemoryBytes { get; init; }

    internal (int Iterations, int MemoryBytes) ResolveKdfParameters()
    {
        if (KdfIterations.HasValue != KdfMemoryBytes.HasValue)
        {
            throw new ArgumentException(
                "KDF iterations and memory must either both be specified or both be omitted.");
        }

        if (KdfIterations is { } iterations && KdfMemoryBytes is { } memoryBytes)
        {
            ValidateKdf(iterations, memoryBytes);
            return (iterations, memoryBytes);
        }

        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        bool constrained = available > 0 && available < 1024L * 1024 * 1024;
        return constrained
            ? (4, 64 * 1024 * 1024)
            : (3, 256 * 1024 * 1024);
    }

    private static void ValidateKdf(int iterations, int memoryBytes)
    {
        if (iterations is < 1 or > LockjawConstants.MaximumKdfIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(KdfIterations),
                $"Argon2id iterations must be between 1 and {LockjawConstants.MaximumKdfIterations}.");
        }

        if (memoryBytes is < LockjawConstants.MinimumKdfMemoryBytes or > LockjawConstants.MaximumKdfMemoryBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(KdfMemoryBytes),
                $"Argon2id memory must be between {LockjawConstants.MinimumKdfMemoryBytes} and " +
                $"{LockjawConstants.MaximumKdfMemoryBytes} bytes.");
        }
    }
}

public sealed record LockjawInspection(
    ushort FormatVersion,
    string Mode,
    string Kdf,
    int KdfIterations,
    int KdfMemoryBytes,
    int PlaintextChunkSize,
    bool Armored);

