using System.Buffers;
using System.Security.Cryptography;

namespace MiYing.Core.Crypto;

/// <summary>
/// 加密视频的只读明文数据流：按需解密数据块并做 LRU 缓存（最多 16 块），
/// 支持随机定位。用于把解密数据直接交给播放器解码，全程不产生明文文件。
/// 本类接管并负责清零传入的密钥数组。
/// </summary>
public sealed class EncryptedVideoStream : Stream
{
    private const int MaxCachedChunks = 16;

    private readonly Stream _source;
    private readonly MsvHeader _header;
    private readonly byte[] _headerBytes;
    private readonly byte[] _fileKey;
    private readonly AesGcm _gcm;
    private readonly object _sync = new();
    private readonly Dictionary<long, byte[]> _cache = new();
    private readonly LinkedList<long> _lru = new();
    private readonly long _dataStart;
    private readonly long _length;

    private long _position;
    private bool _disposed;

    public EncryptedVideoStream(Stream source, byte[] fileKey)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("源流必须可读且支持定位。", nameof(source));
        }

        if (fileKey is null || fileKey.Length != PasswordKeyDerivation.KeyLength)
        {
            throw new ArgumentException("密钥长度无效。", nameof(fileKey));
        }

        try
        {
            (_header, _headerBytes) = MsvHeader.ReadFrom(source);
            _header.ValidateFileLength(source.Length);
            _fileKey = fileKey;
            _gcm = new AesGcm(fileKey, MsvConstants.GcmTagLength);
            _dataStart = _headerBytes.Length;
            _length = _header.PlaintextLength;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>文件头。</summary>
    public MsvHeader Header => _header;

    /// <summary>解密并校验首个数据块，用于尽早确认密码是否正确。</summary>
    public void VerifyContent()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_header.ChunkCount > 0)
            {
                _ = GetChunkLocked(0);
            }
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => Volatile.Read(ref _position);
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "位置不能为负数。");
            }

            Volatile.Write(ref _position, value);
        }
    }

    public override int Read(Span<byte> buffer)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long position = _position;
            int totalCopied = 0;
            while (buffer.Length > 0 && position < _length)
            {
                long chunkIndex = position / _header.ChunkSize;
                int offsetInChunk = (int)(position % _header.ChunkSize);
                byte[] chunk = GetChunkLocked(chunkIndex);
                int available = chunk.Length - offsetInChunk;
                int toCopy = Math.Min(available, buffer.Length);
                chunk.AsSpan(offsetInChunk, toCopy).CopyTo(buffer);
                buffer = buffer[toCopy..];
                position += toCopy;
                totalCopied += toCopy;
            }

            _position = position;
            return totalCopied;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => new(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (target < 0)
            {
                throw new IOException("尝试定位到流的起始位置之前。");
            }

            _position = target;
            return target;
        }
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _gcm.Dispose();
                    CryptographicOperations.ZeroMemory(_fileKey);
                    foreach (byte[] chunk in _cache.Values)
                    {
                        CryptographicOperations.ZeroMemory(chunk);
                    }

                    _cache.Clear();
                    _lru.Clear();
                    _source.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    private byte[] GetChunkLocked(long chunkIndex)
    {
        if (_cache.TryGetValue(chunkIndex, out byte[]? cached))
        {
            _lru.Remove(chunkIndex);
            _lru.AddLast(chunkIndex);
            return cached;
        }

        int want = (int)Math.Min(_header.ChunkSize, _length - chunkIndex * _header.ChunkSize);
        byte[] cipherBuffer = ArrayPool<byte>.Shared.Rent(want + MsvConstants.GcmTagLength);
        byte[] plainBuffer = new byte[want];
        try
        {
            _source.Position = _dataStart + chunkIndex * ((long)_header.ChunkSize + MsvConstants.GcmTagLength);
            MsvIo.ReadExactly(_source, cipherBuffer.AsSpan(0, want + MsvConstants.GcmTagLength));

            Span<byte> nonce = stackalloc byte[MsvConstants.ChunkNonceLength];
            _header.BuildChunkNonce((uint)chunkIndex, nonce);
            try
            {
                _gcm.Decrypt(nonce, cipherBuffer.AsSpan(0, want), cipherBuffer.AsSpan(want, MsvConstants.GcmTagLength), plainBuffer, _headerBytes);
            }
            catch (CryptographicException ex)
            {
                throw chunkIndex == 0
                    ? new MsvInvalidPasswordException(ex)
                    : new MsvCorruptFileException($"第 {chunkIndex + 1} 个数据块未通过完整性校验", ex);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(cipherBuffer, clearArray: false);
        }

        if (_cache.Count >= MaxCachedChunks)
        {
            long oldest = _lru.First!.Value;
            _lru.RemoveFirst();
            if (_cache.Remove(oldest, out byte[]? evicted))
            {
                CryptographicOperations.ZeroMemory(evicted);
            }
        }

        _cache[chunkIndex] = plainBuffer;
        _lru.AddLast(chunkIndex);
        return plainBuffer;
    }
}