namespace Lockjaw.Core;

public class LockjawException : Exception
{
    public LockjawException(string message)
        : base(message)
    {
    }

    public LockjawException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LockjawAuthenticationException : LockjawException
{
    public const string SafeMessage = "Incorrect passphrase or damaged file.";

    public LockjawAuthenticationException()
        : base(SafeMessage)
    {
    }

    public LockjawAuthenticationException(Exception innerException)
        : base(SafeMessage, innerException)
    {
    }
}

public sealed class LockjawFormatException : LockjawException
{
    public LockjawFormatException(string message)
        : base(message)
    {
    }

    public LockjawFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

