using System.Security.Cryptography;
using MiYing.Core.Naming;

namespace MiYing.Core.Crypto;

/// <summary>文件头及其原始字节。</summary>
public sealed record MsvHeaderInfo(MsvHeader Header, byte[] HeaderBytes);

/// <summary>MSV 加密视频文件的高层操作（检查、验密、打开播放流）。</summary>
public static class MsvVideo
{
    private const int StreamBufferSize = 1024 * 1024;

    /// <summary>读取并校验文件头（同时校验文件总长度）。</summary>
    public static MsvHeaderInfo ReadHeader(string path)
    {
        using FileStream stream = OpenRead(path);
        (MsvHeader header, byte[] rawBytes) = MsvHeader.ReadFrom(stream);
        header.ValidateFileLength(stream.Length);
        return new MsvHeaderInfo(header, rawBytes);
    }

    /// <summary>校验密码是否正确（解密首块并验证认证标签）。结构损坏会抛出异常。</summary>
    public static bool VerifyPassword(string path, string password)
    {
        MsvHeaderInfo info = ReadHeader(path);
        if (info.Header.PlaintextLength == 0)
        {
            return true;
        }

        byte[] key = PasswordKeyDerivation.DeriveFileKey(password, info.Header);
        try
        {
            int want = (int)Math.Min(info.Header.ChunkSize, info.Header.PlaintextLength);
            byte[] cipherBuffer = new byte[want + MsvConstants.GcmTagLength];
            byte[] plainBuffer = new byte[want];
            using (FileStream stream = OpenRead(path))
            {
                stream.Position = info.HeaderBytes.Length;
                MsvIo.ReadExactly(stream, cipherBuffer);
            }

            using var gcm = new AesGcm(key, MsvConstants.GcmTagLength);
            Span<byte> nonce = stackalloc byte[MsvConstants.ChunkNonceLength];
            info.Header.BuildChunkNonce(0, nonce);
            try
            {
                gcm.Decrypt(nonce, cipherBuffer.AsSpan(0, want), cipherBuffer.AsSpan(want, MsvConstants.GcmTagLength), plainBuffer, info.HeaderBytes);
            }
            catch (CryptographicException)
            {
                return false;
            }

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 扩展名无法判断时，解密文件头并读取前 32 字节，根据文件签名判断是照片还是视频。
    /// 需要密码；密码错误会抛出 <see cref="MsvInvalidPasswordException"/>。
    /// </summary>
    public static MediaKind DetectMediaKindByContent(string path, string password)
    {
        using EncryptedVideoStream stream = OpenPlaybackStream(path, password);
        Span<byte> header = stackalloc byte[32];
        int read = stream.Read(header);
        return MediaSniffer.Detect(header[..read]);
    }

    /// <summary>
    /// 打开可播放的明文数据流：校验密码后返回按需解密的 <see cref="EncryptedVideoStream"/>。
    /// </summary>
    /// <exception cref="MsvInvalidPasswordException">密码不正确。</exception>
    public static EncryptedVideoStream OpenPlaybackStream(string path, string password)
    {
        MsvHeaderInfo info = ReadHeader(path);
        byte[] key = PasswordKeyDerivation.DeriveFileKey(password, info.Header);
        EncryptedVideoStream? stream = null;
        try
        {
            stream = new EncryptedVideoStream(OpenRead(path), key);
            stream.VerifyContent();
            return stream;
        }
        catch
        {
            stream?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.RandomAccess);
}