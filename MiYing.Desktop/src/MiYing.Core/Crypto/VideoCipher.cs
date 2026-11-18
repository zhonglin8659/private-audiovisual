using System.Buffers;
using System.Security.Cryptography;

namespace MiYing.Core.Crypto;

/// <summary>加密选项。</summary>
public sealed class MsvEncryptOptions
{
    /// <summary>PBKDF2 迭代次数。</summary>
    public int Iterations { get; init; } = MsvConstants.DefaultIterations;

    /// <summary>分块大小（明文字节数）。</summary>
    public int ChunkSize { get; init; } = MsvConstants.DefaultChunkSize;

    /// <summary>写入文件头的原始文件名（解密还原时恢复）。</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>文件级加密完成后是否自动完整解密验证一遍（推荐开启）。</summary>
    public bool VerifyAfterEncrypt { get; init; }
}

/// <summary>处理阶段。</summary>
public enum MsvStage
{
    DeriveKey,
    Encrypt,
    Decrypt,
    Verify,
}

/// <summary>进度报告。</summary>
public readonly record struct MsvProgress(MsvStage Stage, long ProcessedBytes, long TotalBytes)
{
    /// <summary>进度比例（0~1）。</summary>
    public double Fraction => TotalBytes <= 0 ? 0d : Math.Min(1d, (double)ProcessedBytes / TotalBytes);
}

/// <summary>
/// MSV 视频加解密引擎：AES-256-GCM 分块认证加密，支持流式处理与进度回调。
/// </summary>
public static class VideoCipher
{
    private const int StreamBufferSize = 1024 * 1024;

    /// <summary>计算加密后的文件总长度（用于界面预估）。</summary>
    public static long ComputeEncryptedLength(long plaintextLength, int chunkSize, string originalFileName)
    {
        long chunkCount = plaintextLength == 0 ? 0 : (plaintextLength + chunkSize - 1) / chunkSize;
        return MsvHeader.GetHeaderLength(originalFileName) + plaintextLength + chunkCount * MsvConstants.GcmTagLength;
    }

    /// <summary>加密文件（写入临时文件后原子改名，失败或取消时不会留下半成品）。</summary>
    public static async Task<MsvHeader> EncryptFileAsync(string sourcePath, string targetPath, string password, MsvEncryptOptions options, IProgress<MsvProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(options);

        string partPath = targetPath + ".part";
        try
        {
            MsvHeader header;
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                header = await EncryptAsync(input, output, password, options, progress, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (options.VerifyAfterEncrypt)
            {
                await using var verifyInput = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await VerifyAsync(verifyInput, password, progress, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partPath, targetPath, overwrite: true);
            return header;
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    /// <summary>加密流（输入流需可读且支持定位）。</summary>
    public static async Task<MsvHeader> EncryptAsync(Stream input, Stream output, string password, MsvEncryptOptions options, IProgress<MsvProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("密码不能为空。", nameof(password));
        }

        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("输入流必须可读且支持定位。", nameof(input));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("输出流必须可写。", nameof(output));
        }

        if (options.Iterations < MsvConstants.MinimumIterations || options.Iterations > MsvConstants.MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "迭代次数超出合法范围。");
        }

        if (options.ChunkSize < MsvConstants.MinChunkSize || options.ChunkSize > MsvConstants.MaxChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "分块大小超出合法范围。");
        }

        long total = input.Length;
        var header = new MsvHeader
        {
            FormatVersion = MsvConstants.FormatVersion,
            KdfId = MsvConstants.KdfPbkdf2Sha256,
            CipherId = MsvConstants.CipherAes256Gcm,
            Flags = 0,
            KdfIterations = options.Iterations,
            Salt = RandomNumberGenerator.GetBytes(MsvConstants.SaltLength),
            ChunkSize = options.ChunkSize,
            PlaintextLength = total,
            BaseNonce = RandomNumberGenerator.GetBytes(MsvConstants.BaseNonceLength),
            OriginalFileName = options.OriginalFileName ?? string.Empty,
        };

        byte[] headerBytes = header.Serialize();
        progress?.Report(new MsvProgress(MsvStage.DeriveKey, 0, total));
        byte[] key = PasswordKeyDerivation.DeriveFileKey(password, header);
        byte[] inputBuffer = ArrayPool<byte>.Shared.Rent(header.ChunkSize);
        byte[] outputBuffer = ArrayPool<byte>.Shared.Rent(header.ChunkSize + MsvConstants.GcmTagLength);
        byte[] nonce = new byte[MsvConstants.ChunkNonceLength];
        try
        {
            using var gcm = new AesGcm(key, MsvConstants.GcmTagLength);
            await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

            long processed = 0;
            uint chunkIndex = 0;
            while (processed < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int want = (int)Math.Min(header.ChunkSize, total - processed);
                await MsvIo.ReadExactlyAsync(input, inputBuffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
                header.BuildChunkNonce(chunkIndex, nonce);
                Span<byte> tag = outputBuffer.AsSpan(want, MsvConstants.GcmTagLength);
                gcm.Encrypt(nonce, inputBuffer.AsSpan(0, want), outputBuffer.AsSpan(0, want), tag, headerBytes);
                await output.WriteAsync(outputBuffer.AsMemory(0, want + MsvConstants.GcmTagLength), cancellationToken).ConfigureAwait(false);
                processed += want;
                chunkIndex++;
                progress?.Report(new MsvProgress(MsvStage.Encrypt, processed, total));
            }

            return header;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            ArrayPool<byte>.Shared.Return(inputBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(outputBuffer, clearArray: false);
        }
    }

    /// <summary>解密文件（写入临时文件后原子改名，失败或取消时不会留下半成品）。</summary>
    public static async Task<MsvHeader> DecryptFileAsync(string sourcePath, string targetPath, string password, IProgress<MsvProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string partPath = targetPath + ".part";
        try
        {
            MsvHeader header;
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                header = await DecryptAsync(input, output, password, progress, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partPath, targetPath, overwrite: true);
            return header;
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    /// <summary>解密流。</summary>
    public static async Task<MsvHeader> DecryptAsync(Stream input, Stream output, string password, IProgress<MsvProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("密码不能为空。", nameof(password));
        }

        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("输入流必须可读且支持定位。", nameof(input));
        }

        (MsvHeader header, byte[] headerBytes) = MsvHeader.ReadFrom(input);
        header.ValidateFileLength(input.Length);
        progress?.Report(new MsvProgress(MsvStage.DeriveKey, 0, header.PlaintextLength));
        byte[] key = PasswordKeyDerivation.DeriveFileKey(password, header);
        try
        {
            await ProcessChunksAsync(input, output, header, headerBytes, key, MsvStage.Decrypt, progress, cancellationToken).ConfigureAwait(false);
            return header;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 完整验证加密文件：逐块解密并校验认证标签。返回 false 表示密码不正确；结构损坏会抛出异常。
    /// </summary>
    public static async Task<bool> VerifyEncryptedFileAsync(string path, string password, IProgress<MsvProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await VerifyAsync(input, password, progress, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MsvInvalidPasswordException)
        {
            return false;
        }
    }

    private static async Task VerifyAsync(Stream input, string password, IProgress<MsvProgress>? progress, CancellationToken cancellationToken)
    {
        (MsvHeader header, byte[] headerBytes) = MsvHeader.ReadFrom(input);
        header.ValidateFileLength(input.Length);
        progress?.Report(new MsvProgress(MsvStage.Verify, 0, header.PlaintextLength));
        byte[] key = PasswordKeyDerivation.DeriveFileKey(password, header);
        try
        {
            await ProcessChunksAsync(input, null, header, headerBytes, key, MsvStage.Verify, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task ProcessChunksAsync(Stream input, Stream? destination, MsvHeader header, byte[] headerBytes, byte[] key, MsvStage stage, IProgress<MsvProgress>? progress, CancellationToken cancellationToken)
    {
        long total = header.PlaintextLength;
        byte[] cipherBuffer = ArrayPool<byte>.Shared.Rent(header.ChunkSize + MsvConstants.GcmTagLength);
        byte[] plainBuffer = ArrayPool<byte>.Shared.Rent(header.ChunkSize);
        byte[] nonce = new byte[MsvConstants.ChunkNonceLength];
        try
        {
            using var gcm = new AesGcm(key, MsvConstants.GcmTagLength);
            long processed = 0;
            uint chunkIndex = 0;
            while (processed < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int want = (int)Math.Min(header.ChunkSize, total - processed);
                int cipherLength = want + MsvConstants.GcmTagLength;
                try
                {
                    await MsvIo.ReadExactlyAsync(input, cipherBuffer.AsMemory(0, cipherLength), cancellationToken).ConfigureAwait(false);
                }
                catch (MsvCorruptFileException ex)
                {
                    throw new MsvCorruptFileException($"第 {chunkIndex + 1} 个数据块不完整", ex);
                }

                header.BuildChunkNonce(chunkIndex, nonce);
                try
                {
                    gcm.Decrypt(nonce, cipherBuffer.AsSpan(0, want), cipherBuffer.AsSpan(want, MsvConstants.GcmTagLength), plainBuffer.AsSpan(0, want), headerBytes);
                }
                catch (CryptographicException ex)
                {
                    if (chunkIndex == 0)
                    {
                        throw new MsvInvalidPasswordException(ex);
                    }

                    throw new MsvCorruptFileException($"第 {chunkIndex + 1} 个数据块未通过完整性校验", ex);
                }

                if (destination is not null)
                {
                    await destination.WriteAsync(plainBuffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
                }

                processed += want;
                chunkIndex++;
                progress?.Report(new MsvProgress(stage, processed, total));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(cipherBuffer, clearArray: false);
            ArrayPool<byte>.Shared.Return(plainBuffer, clearArray: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}