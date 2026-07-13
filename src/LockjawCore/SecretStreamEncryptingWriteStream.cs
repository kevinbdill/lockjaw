using System.Buffers;
using LibSodium;

namespace Lockjaw.Core;

internal sealed class SecretStreamEncryptingWriteStream : Stream
{
    private readonly Stream _output;
    private readonly SecureMemory<byte> _state;
    private readonly byte[] _authenticatedData;
    private readonly byte[] _plaintext;
    private readonly byte[] _ciphertext;
    private readonly int _chunkSize;
    private int _buffered;
    private bool _completed;
    private bool _disposed;

    public SecretStreamEncryptingWriteStream(
        Stream output,
        SecureMemory<byte> state,
        byte[] authenticatedData,
        int chunkSize)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _authenticatedData = authenticatedData ?? throw new ArgumentNullException(nameof(authenticatedData));
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        _chunkSize = chunkSize;
        _plaintext = ArrayPool<byte>.Shared.Rent(chunkSize);
        _ciphertext = ArrayPool<byte>.Shared.Rent(chunkSize + CryptoSecretStream.OverheadLen);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ThrowIfDisposed();
        _output.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        if (_completed)
        {
            throw new InvalidOperationException("The encrypted stream has already been completed.");
        }

        while (!buffer.IsEmpty)
        {
            // A full buffer is retained until another byte arrives. This lets a
            // full-sized last chunk receive TAG_FINAL without adding framing.
            if (_buffered == _chunkSize)
            {
                EncryptBuffered(CryptoSecretStreamTag.Message);
            }

            int available = _chunkSize - _buffered;
            int toCopy = Math.Min(available, buffer.Length);
            buffer[..toCopy].CopyTo(_plaintext.AsSpan(_buffered));
            _buffered += toCopy;
            buffer = buffer[toCopy..];
        }
    }

    public override void WriteByte(byte value)
    {
        Span<byte> single = stackalloc byte[1];
        single[0] = value;
        Write(single);
    }

    public void Complete()
    {
        ThrowIfDisposed();
        if (_completed)
        {
            return;
        }

        EncryptBuffered(CryptoSecretStreamTag.Final);
        _output.Flush();
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        try
        {
            if (disposing && !_completed)
            {
                Complete();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_plaintext, clearArray: true);
            ArrayPool<byte>.Shared.Return(_ciphertext, clearArray: true);
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    private void EncryptBuffered(CryptoSecretStreamTag tag)
    {
        Span<byte> encrypted = CryptoSecretStream.EncryptChunk(
            _state,
            _ciphertext,
            _plaintext.AsSpan(0, _buffered),
            tag,
            _authenticatedData);
        _output.Write(encrypted);
        _plaintext.AsSpan(0, _buffered).Clear();
        _buffered = 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
