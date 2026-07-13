using System.Formats.Tar;

namespace Lockjaw.Core;

internal static class ArchiveCodec
{
    public static void Write(
        IReadOnlyList<string> inputPaths,
        Stream output,
        CancellationToken cancellationToken)
    {
        if (inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input path is required.", nameof(inputPaths));
        }

        var roots = new HashSet<string>(PathComparer);
        using var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true);

        foreach (string inputPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(inputPath);
            string rootName = GetRootName(fullPath);
            if (!roots.Add(rootName))
            {
                throw new IOException($"Two inputs would use the same archive name: {rootName}");
            }

            WritePath(writer, fullPath, rootName, cancellationToken);
        }
    }

    public static void Extract(
        Stream input,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(stagingDirectory);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var seen = new HashSet<string>(PathComparer);
        var directoryTimes = new List<(string Path, DateTimeOffset Time)>();
        using var reader = new TarReader(input, leaveOpen: true);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = GetSafeDestination(root, rootPrefix, entry.Name);
            if (!seen.Add(destination))
            {
                throw new LockjawFormatException($"The archive contains a duplicate path: {entry.Name}");
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destination);
                    directoryTimes.Add((destination, entry.ModificationTime));
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    if (entry.DataStream is null)
                    {
                        throw new LockjawFormatException($"Archive entry {entry.Name} has no file data.");
                    }

                    string? parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    if (File.Exists(destination) || Directory.Exists(destination))
                    {
                        throw new LockjawFormatException($"The archive path already exists: {entry.Name}");
                    }

                    using (var file = new FileStream(
                        destination,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        128 * 1024,
                        FileOptions.SequentialScan))
                    {
                        StreamHelpers.CopyTo(entry.DataStream, file, cancellationToken);
                        file.Flush(flushToDisk: true);
                    }

                    File.SetLastWriteTimeUtc(destination, entry.ModificationTime.UtcDateTime);
                    break;

                default:
                    throw new LockjawFormatException(
                        $"Archive entry type {entry.EntryType} is not allowed in Lockjaw v1.");
            }
        }

        for (int index = directoryTimes.Count - 1; index >= 0; index--)
        {
            (string path, DateTimeOffset time) = directoryTimes[index];
            Directory.SetLastWriteTimeUtc(path, time.UtcDateTime);
        }
    }

    private static void WritePath(
        TarWriter writer,
        string fullPath,
        string entryName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot read input path: {fullPath}", exception);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Reparse points and symbolic links are not supported in Lockjaw v1: {fullPath}");
        }

        string archiveName = entryName.Replace(Path.DirectorySeparatorChar, '/');
        if ((attributes & FileAttributes.Directory) != 0)
        {
            var directoryEntry = new PaxTarEntry(TarEntryType.Directory, archiveName)
            {
                ModificationTime = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)),
            };
            writer.WriteEntry(directoryEntry);

            string[] children = Directory.GetFileSystemEntries(fullPath);
            Array.Sort(children, StringComparer.Ordinal);
            foreach (string child in children)
            {
                string childName = archiveName + "/" + Path.GetFileName(child);
                WritePath(writer, child, childName, cancellationToken);
            }

            return;
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Input path is not a regular file.", fullPath);
        }

        using var source = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);
        var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, archiveName)
        {
            DataStream = source,
            ModificationTime = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)),
        };
        writer.WriteEntry(fileEntry);
    }

    private static string GetRootName(string fullPath)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(fullPath);
        string name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new IOException("A filesystem root cannot be encrypted directly. Select its contents instead.");
        }

        return name;
    }

    private static string GetSafeDestination(string root, string rootPrefix, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.IndexOf('\0') >= 0)
        {
            throw new LockjawFormatException("The archive contains an invalid path.");
        }

        string normalized = entryName.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0 ||
            normalized.StartsWith('/') ||
            normalized.IndexOf(':') >= 0 ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new LockjawFormatException($"The archive contains an unsafe path: {entryName}");
        }

        string relative = normalized.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
        {
            throw new LockjawFormatException($"The archive contains a rooted path: {entryName}");
        }

        string destination = Path.GetFullPath(Path.Combine(root, relative));
        if (!destination.StartsWith(rootPrefix, PathComparison))
        {
            throw new LockjawFormatException($"The archive path escapes the output directory: {entryName}");
        }

        return destination;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
