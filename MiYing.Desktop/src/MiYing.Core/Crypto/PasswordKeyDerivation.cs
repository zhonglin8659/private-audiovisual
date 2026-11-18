using System.Security.Cryptography;
using System.Text;

namespace MiYing.Core.Crypto;

/// <summary>
/// 基于密码派生文件加密密钥（PBKDF2-HMAC-SHA256，盐值随文件随机生成）。
/// </summary>
public static class PasswordKeyDerivation
{
    /// <summary>派生密钥长度（AES-256 需要 32 字节）。</summary>
    public const int KeyLength = 32;

    public static byte[] DeriveFileKey(string password, MsvHeader header)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(header);
        return DeriveFileKey(Encoding.UTF8.GetBytes(password), header);
    }

    public static byte[] DeriveFileKey(ReadOnlySpan<byte> passwordBytes, MsvHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            header.Salt,
            header.KdfIterations,
            HashAlgorithmName.SHA256,
            KeyLength);
    }
}