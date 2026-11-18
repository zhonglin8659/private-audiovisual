using System.IO;
using System.Runtime.InteropServices;

namespace MiYing.Encryptor.Services;

/// <summary>把文件移入回收站（用户可随时还原），失败时原文件保持不变。</summary>
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStructW
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStructW fileOp);

    /// <summary>尝试把文件移入回收站；返回是否成功。</summary>
    public static bool TryMoveToRecycleBin(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return false;
            }

            var op = new ShFileOpStructW
            {
                wFunc = FoDelete,
                pFrom = Path.GetFullPath(path) + "\0\0",
                fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
            };

            int result = SHFileOperationW(ref op);
            if (result != 0 || op.fAnyOperationsAborted)
            {
                error = "移入回收站失败（错误码 " + result + "）";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}