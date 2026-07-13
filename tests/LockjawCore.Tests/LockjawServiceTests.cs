using System.Text;
using Lockjaw.Core;
using Xunit;

namespace LockjawCore.Tests;

public sealed class LockjawServiceTests : IDisposable
{
    private static readonly LockjawEncryptionOptions FastOptions = new()
    {
        KdfIterations = 1,
        KdfMemoryBytes = LockjawConstants.MinimumKdfMemoryBytes,
    };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "lockjaw-tests-" + Guid.NewGuid().ToString("N"));

    public LockjawServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void FolderRoundTripPreservesFileBytesAndTimestamps()
    {
        string source = CreateSampleTree();
        string encrypted = Path.Combine(_root, "sample.lockjaw");
        string recovered = Path.Combine(_root, "recovered");
        char[] passphrase = "correct horse battery staple".ToCharArray();

        try
        {
            LockjawService.Encrypt([source], encrypted, passphrase, FastOptions);
            LockjawService.Decrypt(encrypted, recovered, passphrase);
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }

        string restoredRoot = Path.Combine(recovered, Path.GetFileName(source));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "hello.txt")),
            File.ReadAllBytes(Path.Combine(restoredRoot, "hello.txt")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "nested", "random.bin")),
            File.ReadAllBytes(Path.Combine(restoredRoot, "nested", "random.bin")));

        DateTime expected = File.GetLastWriteTimeUtc(Path.Combine(source, "hello.txt"));
        DateTime actual = File.GetLastWriteTimeUtc(Path.Combine(restoredRoot, "hello.txt"));
        Assert.InRange(Math.Abs((actual - expected).TotalSeconds), 0, 1);
    }

    [Fact]
    public void WrongPassphraseAndTamperingUseTheSameFailure()
    {
        string source = CreateTextFile("message.txt", "classified");
        string encrypted = Path.Combine(_root, "message.lockjaw");
        char[] correct = "this is the correct passphrase".ToCharArray();
        char[] wrong = "this is the incorrect passphrase".ToCharArray();

        try
        {
            LockjawService.Encrypt([source], encrypted, correct, FastOptions);

            LockjawAuthenticationException wrongFailure = Assert.Throws<LockjawAuthenticationException>(
                () => LockjawService.Decrypt(encrypted, Path.Combine(_root, "wrong-output"), wrong));

            byte[] bytes = File.ReadAllBytes(encrypted);
            bytes[^20] ^= 0x01;
            string tampered = Path.Combine(_root, "tampered.lockjaw");
            File.WriteAllBytes(tampered, bytes);

            LockjawAuthenticationException tamperFailure = Assert.Throws<LockjawAuthenticationException>(
                () => LockjawService.Decrypt(tampered, Path.Combine(_root, "tampered-output"), correct));

            Assert.Equal(wrongFailure.Message, tamperFailure.Message);
            Assert.False(Directory.Exists(Path.Combine(_root, "wrong-output")));
            Assert.False(Directory.Exists(Path.Combine(_root, "tampered-output")));
        }
        finally
        {
            correct.AsSpan().Clear();
            wrong.AsSpan().Clear();
        }
    }

    [Fact]
    public void TruncationFailsWithoutAnOutputDirectory()
    {
        string source = CreateTextFile("truncate.txt", new string('x', 50_000));
        string encrypted = Path.Combine(_root, "truncate.lockjaw");
        string truncated = Path.Combine(_root, "truncated.lockjaw");
        string output = Path.Combine(_root, "truncated-output");
        char[] passphrase = "a reasonably long passphrase".ToCharArray();

        try
        {
            LockjawService.Encrypt([source], encrypted, passphrase, FastOptions);
            byte[] bytes = File.ReadAllBytes(encrypted);
            File.WriteAllBytes(truncated, bytes.AsSpan(0, bytes.Length - 1).ToArray());

            Assert.Throws<LockjawAuthenticationException>(
                () => LockjawService.Decrypt(truncated, output, passphrase));
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    [Fact]
    public void PublicHeaderTamperingIsAuthenticated()
    {
        string source = CreateTextFile("header.txt", "header authentication");
        string encrypted = Path.Combine(_root, "header.lockjaw");
        string tampered = Path.Combine(_root, "header-tampered.lockjaw");
        char[] passphrase = "authenticate every public header field".ToCharArray();

        try
        {
            LockjawService.Encrypt([source], encrypted, passphrase, FastOptions);
            byte[] bytes = File.ReadAllBytes(encrypted);
            int saltOffset = LockjawConstants.PreambleLength + 9;
            bytes[saltOffset] ^= 0x80;
            File.WriteAllBytes(tampered, bytes);

            Assert.Throws<LockjawAuthenticationException>(
                () => LockjawService.Decrypt(tampered, Path.Combine(_root, "header-output"), passphrase));
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    [Fact]
    public void ArmorRoundTripAndInspectionWork()
    {
        string source = CreateTextFile("armored.txt", "email-safe armored ciphertext");
        string encrypted = Path.Combine(_root, "armored.lockjaw.txt");
        string output = Path.Combine(_root, "armor-output");
        char[] passphrase = "armor mode passphrase".ToCharArray();
        LockjawEncryptionOptions options = FastOptions with { Armor = true };

        try
        {
            LockjawService.Encrypt([source], encrypted, passphrase, options);
            string armor = File.ReadAllText(encrypted, Encoding.UTF8);
            Assert.StartsWith(LockjawConstants.ArmorBegin, armor);
            Assert.Contains(LockjawConstants.ArmorEnd, armor);

            LockjawInspection inspection = LockjawService.Inspect(encrypted);
            Assert.True(inspection.Armored);
            Assert.Equal(LockjawConstants.FormatVersion, inspection.FormatVersion);
            Assert.Equal(LockjawConstants.MinimumKdfMemoryBytes, inspection.KdfMemoryBytes);

            LockjawService.Decrypt(encrypted, output, passphrase);
            Assert.Equal(
                "email-safe armored ciphertext",
                File.ReadAllText(Path.Combine(output, "armored.txt"), Encoding.UTF8));
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    [Fact]
    public void NoCompressionRoundTripWorks()
    {
        string source = CreateTextFile("plain.txt", "not compressed");
        string encrypted = Path.Combine(_root, "plain.lockjaw");
        string output = Path.Combine(_root, "plain-output");
        char[] passphrase = "no compression passphrase".ToCharArray();
        LockjawEncryptionOptions options = FastOptions with { Compress = false };

        try
        {
            LockjawService.Encrypt([source], encrypted, passphrase, options);
            LockjawService.Decrypt(encrypted, output, passphrase);
            Assert.Equal("not compressed", File.ReadAllText(Path.Combine(output, "plain.txt")));
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateSampleTree()
    {
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "hello.txt"), "Hello from Lockjaw", Encoding.UTF8);

        byte[] random = new byte[256 * 1024];
        Random.Shared.NextBytes(random);
        File.WriteAllBytes(Path.Combine(source, "nested", "random.bin"), random);

        DateTime timestamp = new(2026, 7, 13, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(source, "hello.txt"), timestamp);
        return source;
    }

    private string CreateTextFile(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }
}
