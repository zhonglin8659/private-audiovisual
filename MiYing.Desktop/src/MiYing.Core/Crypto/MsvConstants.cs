namespace MiYing.Core.Crypto;

/// <summary>
/// MSV（MiYing Secure Video）文件格式常量。
/// </summary>
public static class MsvConstants
{
    /// <summary>文件头魔数："MSV1"。</summary>
    public static ReadOnlySpan<byte> Magic => "MSV1"u8;

    /// <summary>当前格式版本。</summary>
    public const byte FormatVersion = 1;

    /// <summary>密钥派生算法：PBKDF2-HMAC-SHA256。</summary>
    public const byte KdfPbkdf2Sha256 = 1;

    /// <summary>加密算法：AES-256-GCM（分块认证加密）。</summary>
    public const byte CipherAes256Gcm = 1;

    /// <summary>默认 PBKDF2 迭代次数。</summary>
    public const int DefaultIterations = 600_000;

    public const int MinimumIterations = 100_000;
    public const int MaximumIterations = 5_000_000;

    /// <summary>默认分块大小：1 MiB（明文字节数）。</summary>
    public const int DefaultChunkSize = 1024 * 1024;

    public const int MinChunkSize = 64 * 1024;
    public const int MaxChunkSize = 16 * 1024 * 1024;

    public const int SaltLength = 16;
    public const int BaseNonceLength = 8;
    public const int ChunkNonceLength = 12;
    public const int GcmTagLength = 16;

    /// <summary>原始文件名在文件头中可存储的最大字节数（UTF-8 编码后）。</summary>
    public const int FileNameMaxBytes = 1024;

    /// <summary>加密文件扩展名。</summary>
    public const string FileExtension = ".msv";
}