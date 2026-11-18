using System.Buffers.Binary;
using System.Text;

namespace MiYing.Core.Crypto;

/// <summary>
/// MSV 文件头（明文存储，整体作为每个数据块的 GCM 附加认证数据 AAD，任何字段被篡改都会导致解密失败）。
///
/// 布局（大端序）：
///   偏移  长度  字段
///   0     4     魔数 "MSV1"
///   4     1     格式版本
///   5     1     KDF 标识
///   6     1     加密算法标识
///   7     1     标志位（保留）
///   8     4     KDF 迭代次数
///   12    16    盐值
///   28    4     分块大小（明文字节数）
///   32    8     明文总长度
///   40    8     基础 Nonce（每块 Nonce = 基础 Nonce(8 字节) + 块序号(4 字节)，全局唯一）
///   48    2     原始文件名长度（字节）
///   50    N     原始文件名（UTF-8）
/// </summary>
public sealed class MsvHeader
{
    /// <summary>文件头固定部分长度。</summary>
    public const int FixedSize = 50;

    public byte FormatVersion { get; init; } = MsvConstants.FormatVersion;

    public byte KdfId { get; init; } = MsvConstants.KdfPbkdf2Sha256;

    public byte CipherId { get; init; } = MsvConstants.CipherAes256Gcm;

    /// <summary>标志位，当前全部保留（bit0 预留：机器绑定）。</summary>
    public byte Flags { get; init; }

    public int KdfIterations { get; init; } = MsvConstants.DefaultIterations;

    public byte[] Salt { get; init; } = Array.Empty<byte>();

    public int ChunkSize { get; init; } = MsvConstants.DefaultChunkSize;

    /// <summary>明文（原始视频）总字节数。</summary>
    public long PlaintextLength { get; init; }

    public byte[] BaseNonce { get; init; } = Array.Empty<byte>();

    /// <summary>原始文件名（用于解密还原时恢复文件名）。</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>数据块数量。</summary>
    public long ChunkCount => PlaintextLength == 0
        ? 0
        : (PlaintextLength + ChunkSize - 1) / ChunkSize;

    /// <summary>整个加密文件的预期长度：文件头 + 明文 + 每块 16 字节认证标签。</summary>
    public long TotalLength => GetHeaderLength(OriginalFileName)
        + PlaintextLength
        + ChunkCount * MsvConstants.GcmTagLength;

    /// <summary>计算包含指定文件名的文件头总字节数。</summary>
    public static int GetHeaderLength(string fileName) => FixedSize + GetFileNameBytes(fileName).Length;

    /// <summary>生成数据块的 GCM Nonce（基础 Nonce + 大端序块序号）。</summary>
    public void BuildChunkNonce(uint chunkIndex, Span<byte> destination)
    {
        if (BaseNonce.Length != MsvConstants.BaseNonceLength)
        {
            throw new InvalidOperationException("文件头 BaseNonce 长度无效。");
        }

        if (destination.Length < MsvConstants.ChunkNonceLength)
        {
            throw new ArgumentException("目标缓冲区过小。", nameof(destination));
        }

        BaseNonce.CopyTo(destination);
        BinaryPrimitives.WriteUInt32BigEndian(destination[MsvConstants.BaseNonceLength..], chunkIndex);
    }

    /// <summary>序列化为字节数组。</summary>
    public byte[] Serialize()
    {
        if (Salt.Length != MsvConstants.SaltLength)
        {
            throw new InvalidOperationException("文件头 Salt 长度无效。");
        }

        if (BaseNonce.Length != MsvConstants.BaseNonceLength)
        {
            throw new InvalidOperationException("文件头 BaseNonce 长度无效。");
        }

        byte[] nameBytes = GetFileNameBytes(OriginalFileName);
        var buffer = new byte[FixedSize + nameBytes.Length];
        Span<byte> span = buffer;

        MsvConstants.Magic.CopyTo(span);
        span[4] = FormatVersion;
        span[5] = KdfId;
        span[6] = CipherId;
        span[7] = Flags;
        BinaryPrimitives.WriteInt32BigEndian(span[8..], KdfIterations);
        Salt.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32BigEndian(span[28..], ChunkSize);
        BinaryPrimitives.WriteInt64BigEndian(span[32..], PlaintextLength);
        BaseNonce.CopyTo(span[40..]);
        BinaryPrimitives.WriteUInt16BigEndian(span[48..], (ushort)nameBytes.Length);
        nameBytes.CopyTo(span[50..]);

        return buffer;
    }

    /// <summary>从字节序列解析并校验文件头。</summary>
    /// <exception cref="MsvNotMsvFileException">不是 MSV 文件。</exception>
    /// <exception cref="MsvUnsupportedFormatException">格式版本或算法不受支持。</exception>
    /// <exception cref="MsvCorruptFileException">文件头字段非法。</exception>
    public static (MsvHeader Header, byte[] RawBytes) Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize || !data[..4].SequenceEqual(MsvConstants.Magic))
        {
            throw new MsvNotMsvFileException();
        }

        byte version = data[4];
        if (version == 0 || version > MsvConstants.FormatVersion)
        {
            throw new MsvUnsupportedFormatException($"格式版本 {version}");
        }

        byte kdfId = data[5];
        if (kdfId != MsvConstants.KdfPbkdf2Sha256)
        {
            throw new MsvUnsupportedFormatException($"密钥派生算法 {kdfId}");
        }

        byte cipherId = data[6];
        if (cipherId != MsvConstants.CipherAes256Gcm)
        {
            throw new MsvUnsupportedFormatException($"加密算法 {cipherId}");
        }

        byte flags = data[7];
        if ((flags & 0b1111_1110) != 0)
        {
            throw new MsvUnsupportedFormatException("未知标志位");
        }

        int iterations = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
        if (iterations < MsvConstants.MinimumIterations || iterations > MsvConstants.MaximumIterations)
        {
            throw new MsvCorruptFileException("迭代次数超出合法范围");
        }

        int chunkSize = BinaryPrimitives.ReadInt32BigEndian(data[28..]);
        if (chunkSize < MsvConstants.MinChunkSize || chunkSize > MsvConstants.MaxChunkSize)
        {
            throw new MsvCorruptFileException("分块大小超出合法范围");
        }

        long plaintextLength = BinaryPrimitives.ReadInt64BigEndian(data[32..]);
        if (plaintextLength < 0)
        {
            throw new MsvCorruptFileException("明文长度无效");
        }

        int nameLength = BinaryPrimitives.ReadUInt16BigEndian(data[48..]);
        if (nameLength > MsvConstants.FileNameMaxBytes)
        {
            throw new MsvCorruptFileException("文件名长度超出限制");
        }

        if (data.Length < FixedSize + nameLength)
        {
            throw new MsvCorruptFileException("文件头不完整");
        }

        var header = new MsvHeader
        {
            FormatVersion = version,
            KdfId = kdfId,
            CipherId = cipherId,
            Flags = flags,
            KdfIterations = iterations,
            Salt = data.Slice(12, MsvConstants.SaltLength).ToArray(),
            ChunkSize = chunkSize,
            PlaintextLength = plaintextLength,
            BaseNonce = data.Slice(40, MsvConstants.BaseNonceLength).ToArray(),
            OriginalFileName = nameLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(data.Slice(FixedSize, nameLength)),
        };

        if (header.ChunkCount > uint.MaxValue)
        {
            throw new MsvUnsupportedFormatException("明文长度超出支持范围");
        }

        byte[] rawBytes = data[..(FixedSize + nameLength)].ToArray();
        return (header, rawBytes);
    }

    /// <summary>从流的起始位置读取并解析文件头；读取完成后流位置位于数据区起点。</summary>
    public static (MsvHeader Header, byte[] RawBytes) ReadFrom(Stream stream)
    {
        if (!stream.CanSeek)
        {
            throw new ArgumentException("流必须支持定位。", nameof(stream));
        }

        stream.Position = 0;
        var fixedBuffer = new byte[FixedSize];
        if (!MsvIo.TryReadExactly(stream, fixedBuffer))
        {
            throw new MsvNotMsvFileException();
        }

        if (!fixedBuffer.AsSpan(0, 4).SequenceEqual(MsvConstants.Magic))
        {
            throw new MsvNotMsvFileException();
        }

        int nameLength = BinaryPrimitives.ReadUInt16BigEndian(fixedBuffer.AsSpan(48));
        if (nameLength > MsvConstants.FileNameMaxBytes)
        {
            throw new MsvCorruptFileException("文件名长度超出限制");
        }

        var fullBuffer = new byte[FixedSize + nameLength];
        fixedBuffer.CopyTo(fullBuffer, 0);
        if (nameLength > 0 && !MsvIo.TryReadExactly(stream, fullBuffer.AsSpan(FixedSize)))
        {
            throw new MsvCorruptFileException("文件头不完整");
        }

        return Parse(fullBuffer);
    }

    /// <summary>校验实际文件长度与文件头声明是否一致（防止截断或尾部追加）。</summary>
    public void ValidateFileLength(long actualLength)
    {
        long expected = TotalLength;
        if (actualLength < expected)
        {
            throw new MsvCorruptFileException("文件长度不足（可能被截断）");
        }

        if (actualLength > expected)
        {
            throw new MsvCorruptFileException("文件尾部存在多余数据");
        }
    }

    private static byte[] GetFileNameBytes(string fileName)
        => TruncateUtf8(fileName ?? string.Empty, MsvConstants.FileNameMaxBytes);

    /// <summary>按 UTF-8 字节数截断字符串，且不切断多字节字符。</summary>
    public static byte[] TruncateUtf8(string value, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return bytes;
        }

        int length = maxBytes;
        while (length > 0 && (bytes[length] & 0b1100_0000) == 0b1000_0000)
        {
            length--;
        }

        return bytes[..length];
    }
}