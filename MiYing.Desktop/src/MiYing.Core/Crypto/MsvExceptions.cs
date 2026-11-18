namespace MiYing.Core.Crypto;

/// <summary>密影加密相关异常基类。</summary>
public class MsvException : Exception
{
    public MsvException(string message) : base(message) { }

    public MsvException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>目标文件不是 MSV 加密文件。</summary>
public sealed class MsvNotMsvFileException : MsvException
{
    public MsvNotMsvFileException() : base("不是有效的 .msv 加密视频文件。") { }
}

/// <summary>文件使用了当前版本不支持的加密格式。</summary>
public sealed class MsvUnsupportedFormatException : MsvException
{
    public MsvUnsupportedFormatException(string detail) : base($"不支持的加密格式：{detail}。") { }
}

/// <summary>文件结构损坏、被截断或被篡改。</summary>
public sealed class MsvCorruptFileException : MsvException
{
    public MsvCorruptFileException(string detail) : base($"文件已损坏或已被篡改：{detail}。") { }

    public MsvCorruptFileException(string detail, Exception innerException)
        : base($"文件已损坏或已被篡改：{detail}。", innerException) { }
}

/// <summary>密码不正确（首个数据块未通过认证）。</summary>
public sealed class MsvInvalidPasswordException : MsvException
{
    public MsvInvalidPasswordException() : base("密码不正确，或文件已损坏。") { }

    public MsvInvalidPasswordException(Exception innerException) : base("密码不正确，或文件已损坏。", innerException) { }
}