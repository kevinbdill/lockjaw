using Lockjaw.Core;

namespace Lockjaw.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitAuthentication = 1;
    private const int ExitCorruptOrUnsupported = 2;
    private const int ExitIo = 3;
    private const int ExitUsage = 64;
    private const int ExitInternal = 70;

    public static int Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return ExitSuccess;
            }

            if (args[0] is "--version" or "-V")
            {
                Console.WriteLine("Lockjaw M1 (format v1)");
                return ExitSuccess;
            }

            return args[0].ToLowerInvariant() switch
            {
                "encrypt" => RunEncrypt(args[1..], cancellation.Token),
                "decrypt" => RunDecrypt(args[1..], cancellation.Token),
                "inspect" => RunInspect(args[1..], cancellation.Token),
                _ => throw new UsageException($"Unknown command: {args[0]}"),
            };
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            Console.Error.WriteLine("Run 'Lockjaw --help' for usage.");
            return ExitUsage;
        }
        catch (LockjawAuthenticationException exception)
        {
            // Deliberately one message and one exit code: do not create a
            // passphrase-vs-corruption oracle.
            Console.Error.WriteLine(exception.Message);
            return ExitAuthentication;
        }
        catch (LockjawFormatException exception)
        {
            Console.Error.WriteLine($"Invalid Lockjaw file: {exception.Message}");
            return ExitCorruptOrUnsupported;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled. No output was created.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"I/O error: {exception.Message}");
            return ExitIo;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unexpected error: {exception.Message}");
            return ExitInternal;
        }
    }

    private static int RunEncrypt(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = ParsedArguments.Parse(args, allowMultiplePaths: true);
        if (parsed.Paths.Count == 0)
        {
            throw new UsageException("encrypt requires at least one file or folder.");
        }

        if (parsed.IdentityPath is not null || parsed.Recipients.Count != 0)
        {
            throw new UsageException("Recipient key mode is scheduled for M4 and is not present in this M1 build.");
        }

        string output = parsed.OutputPath ?? GetDefaultEncryptedOutput(parsed.Paths, parsed.Armor);
        char[] passphrase = ReadPassphrase(confirm: true);
        try
        {
            var options = new LockjawEncryptionOptions
            {
                Armor = parsed.Armor,
                Compress = !parsed.NoCompress,
            };

            LockjawService.Encrypt(parsed.Paths, output, passphrase, options, cancellationToken);
            Console.WriteLine($"Encrypted: {Path.GetFullPath(output)}");
            return ExitSuccess;
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    private static int RunDecrypt(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = ParsedArguments.Parse(args, allowMultiplePaths: false);
        if (parsed.Paths.Count != 1)
        {
            throw new UsageException("decrypt requires exactly one .lockjaw file.");
        }

        if (parsed.Armor || parsed.NoCompress || parsed.Recipients.Count != 0)
        {
            throw new UsageException("The supplied option is not valid for decrypt.");
        }

        if (parsed.IdentityPath is not null)
        {
            throw new UsageException("Identity key mode is scheduled for M4 and is not present in this M1 build.");
        }

        string output = parsed.OutputPath ?? GetDefaultDecryptedOutput(parsed.Paths[0]);
        char[] passphrase = ReadPassphrase(confirm: false);
        try
        {
            LockjawService.Decrypt(parsed.Paths[0], output, passphrase, cancellationToken);
            Console.WriteLine($"Decrypted: {Path.GetFullPath(output)}");
            return ExitSuccess;
        }
        finally
        {
            passphrase.AsSpan().Clear();
        }
    }

    private static int RunInspect(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            throw new UsageException("inspect requires exactly one .lockjaw file.");
        }

        LockjawInspection inspection = LockjawService.Inspect(args[0], cancellationToken);
        Console.WriteLine($"Format version : {inspection.FormatVersion}");
        Console.WriteLine($"Mode           : {inspection.Mode}");
        Console.WriteLine($"KDF            : {inspection.Kdf}");
        Console.WriteLine($"KDF iterations : {inspection.KdfIterations}");
        Console.WriteLine($"KDF memory     : {inspection.KdfMemoryBytes / (1024 * 1024)} MiB");
        Console.WriteLine($"Chunk size     : {inspection.PlaintextChunkSize / (1024 * 1024)} MiB");
        Console.WriteLine($"Armored        : {(inspection.Armored ? "yes" : "no")}");
        return ExitSuccess;
    }

    private static char[] ReadPassphrase(bool confirm)
    {
        string? environmentValue = Environment.GetEnvironmentVariable("LOCKJAW_PASSPHRASE");
        if (environmentValue is not null)
        {
            if (environmentValue.Length == 0)
            {
                throw new UsageException("LOCKJAW_PASSPHRASE is set but empty.");
            }

            return environmentValue.ToCharArray();
        }

        if (Console.IsInputRedirected)
        {
            throw new UsageException(
                "An interactive terminal is required. For automation, set LOCKJAW_PASSPHRASE.");
        }

        char[] first = ReadHiddenLine("Passphrase: ");
        if (first.Length == 0)
        {
            first.AsSpan().Clear();
            throw new UsageException("The passphrase cannot be empty.");
        }

        if (!confirm)
        {
            return first;
        }

        char[] second = ReadHiddenLine("Confirm passphrase: ");
        bool matches = first.AsSpan().SequenceEqual(second);
        second.AsSpan().Clear();
        if (!matches)
        {
            first.AsSpan().Clear();
            throw new UsageException("Passphrases do not match.");
        }

        return first;
    }

    private static char[] ReadHiddenLine(string prompt)
    {
        Console.Error.Write(prompt);
        char[] buffer = new char[128];
        int length = 0;
        try
        {
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.Error.WriteLine();
                    return buffer.AsSpan(0, length).ToArray();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0)
                    {
                        buffer[--length] = '\0';
                    }

                    continue;
                }

                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }

                if (length == buffer.Length)
                {
                    char[] expanded = new char[checked(buffer.Length * 2)];
                    buffer.CopyTo(expanded, 0);
                    buffer.AsSpan().Clear();
                    buffer = expanded;
                }

                buffer[length++] = key.KeyChar;
            }
        }
        finally
        {
            buffer.AsSpan().Clear();
        }
    }

    private static string GetDefaultEncryptedOutput(IReadOnlyList<string> paths, bool armor)
    {
        if (paths.Count != 1)
        {
            throw new UsageException("-o is required when encrypting multiple inputs.");
        }

        string fullPath = Path.GetFullPath(paths[0]);
        string trimmed = Path.TrimEndingDirectorySeparator(fullPath);
        return trimmed + (armor ? LockjawConstants.ArmoredExtension : LockjawConstants.BinaryExtension);
    }

    private static string GetDefaultDecryptedOutput(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);
        string parent = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string name = Path.GetFileName(fullPath);
        if (name.EndsWith(LockjawConstants.ArmoredExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^LockjawConstants.ArmoredExtension.Length];
        }
        else if (name.EndsWith(LockjawConstants.BinaryExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^LockjawConstants.BinaryExtension.Length];
        }

        if (name.Length == 0)
        {
            name = "lockjaw";
        }

        return Path.Combine(parent, name + "-decrypted");
    }

    private static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Lockjaw M1 - authenticated file encryption

            Usage:
              Lockjaw encrypt <paths...> [-o out.lockjaw] [-p] [--armor] [--no-compress]
              Lockjaw decrypt <file.lockjaw> [-o outdir] [-p]
              Lockjaw inspect <file.lockjaw>

            Passphrases are prompted without echo. For non-interactive use, set
            LOCKJAW_PASSPHRASE (less safe because environment variables can leak).

            M1 implements passphrase mode. Recipient identities and key management
            are intentionally deferred to M4.
            """);
    }

    private sealed class ParsedArguments
    {
        public List<string> Paths { get; } = [];
        public List<string> Recipients { get; } = [];
        public string? OutputPath { get; private set; }
        public string? IdentityPath { get; private set; }
        public bool Armor { get; private set; }
        public bool NoCompress { get; private set; }

        public static ParsedArguments Parse(string[] args, bool allowMultiplePaths)
        {
            var result = new ParsedArguments();
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument)
                {
                    case "-o":
                    case "--output":
                        result.OutputPath = ReadOptionValue(args, ref index, argument);
                        break;

                    case "-p":
                    case "--passphrase":
                        break;

                    case "--armor":
                        result.Armor = true;
                        break;

                    case "--no-compress":
                        result.NoCompress = true;
                        break;

                    case "-r":
                    case "--recipient":
                        result.Recipients.Add(ReadOptionValue(args, ref index, argument));
                        break;

                    case "-i":
                    case "--identity":
                        result.IdentityPath = ReadOptionValue(args, ref index, argument);
                        break;

                    default:
                        if (argument.StartsWith('-'))
                        {
                            throw new UsageException($"Unknown option: {argument}");
                        }

                        result.Paths.Add(argument);
                        break;
                }
            }

            if (!allowMultiplePaths && result.Paths.Count > 1)
            {
                throw new UsageException("Too many input paths were supplied.");
            }

            return result;
        }

        private static string ReadOptionValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || args[index].StartsWith('-'))
            {
                throw new UsageException($"{option} requires a value.");
            }

            return args[index];
        }
    }

    private sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }
}
