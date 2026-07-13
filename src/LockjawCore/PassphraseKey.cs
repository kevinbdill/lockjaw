using System.Text;
using LibSodium;

namespace Lockjaw.Core;

internal static class PassphraseKey
{
    public static SecureMemory<byte> Derive(ReadOnlySpan<char> passphrase, LockjawHeader header)
    {
        if (passphrase.IsEmpty)
        {
            throw new ArgumentException("The passphrase cannot be empty.", nameof(passphrase));
        }

        int passwordByteCount = Encoding.UTF8.GetByteCount(passphrase);
        using var password = new SecureMemory<byte>(passwordByteCount);
        Encoding.UTF8.GetBytes(passphrase, password.AsSpan());

        var key = new SecureMemory<byte>(CryptoSecretStream.KeyLen);
        try
        {
            CryptoPasswordHashArgon.DeriveKey(
                key,
                password,
                header.Salt,
                header.KdfIterations,
                header.KdfMemoryBytes,
                PasswordHashArgonAlgorithm.Argon2id13);
            key.ProtectReadOnly();
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            password.MemZero();
        }
    }
}

