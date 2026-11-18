namespace MiYing.Core.Crypto;

/// <summary>内部 IO 辅助方法。</summary>
internal static class MsvIo
{
    /// <summary>读取指定数量的字节；返回 false 表示流提前结束。</summary>
    public static bool TryReadExactly(Stream stream, Span<byte> buffer) => TryReadExactly(stream, buffer, out _);

    /// <summary>读取指定数量的字节；返回 false 表示流提前结束。</summary>
    public static bool TryReadExactly(Stream stream, Span<byte> buffer, out int totalRead)
    {
        totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read <= 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    /// <summary>读取指定数量的字节；流提前结束则抛出 <see cref="MsvCorruptFileException"/>。</summary>
    public static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        if (!TryReadExactly(stream, buffer))
        {
            throw new MsvCorruptFileException("数据意外结束");
        }
    }

    /// <summary>异步读取指定数量的字节；流提前结束则抛出 <see cref="MsvCorruptFileException"/>。</summary>
    public static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new MsvCorruptFileException("数据意外结束");
            }

            totalRead += read;
        }
    }
}