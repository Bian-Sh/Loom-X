using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LoomX.Configuration;

public static class ProtectedApiKeyStore
{
    public const string Prefix = "dpapi:";

    public static bool IsProtectedValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plainText)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected API key storage is only supported on Windows.");
        var input = DATA_BLOB.From(Encoding.UTF8.GetBytes(plainText));
        var output = new DATA_BLOB();
        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)) throw new CryptographicException(Marshal.GetLastWin32Error());
            return Prefix + Convert.ToBase64String(output.ToArray());
        }
        finally { input.Free(); output.Free(); }
    }

    public static string Unprotect(string value)
    {
        if (!IsProtectedValue(value)) return value;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected API key storage is only supported on Windows.");
        var input = DATA_BLOB.From(Convert.FromBase64String(value[Prefix.Length..]));
        var output = new DATA_BLOB();
        try
        {
            if (!CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)) throw new CryptographicException(Marshal.GetLastWin32Error());
            return Encoding.UTF8.GetString(output.ToArray());
        }
        finally { input.Free(); output.Free(); }
    }

    public static bool TryUnprotect(string value, out string plainText)
    {
        try
        {
            plainText = Unprotect(value);
            return true;
        }
        catch (CryptographicException) when (IsProtectedValue(value))
        {
            plainText = string.Empty;
            return false;
        }
        catch (FormatException) when (IsProtectedValue(value))
        {
            plainText = string.Empty;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
        public static DATA_BLOB From(byte[] data) { var blob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) }; Marshal.Copy(data, 0, blob.pbData, data.Length); return blob; }
        public readonly byte[] ToArray() { if (cbData <= 0 || pbData == IntPtr.Zero) return []; var data = new byte[cbData]; Marshal.Copy(pbData, data, 0, cbData); return data; }
        public void Free() { if (pbData != IntPtr.Zero) { Marshal.FreeHGlobal(pbData); pbData = IntPtr.Zero; cbData = 0; } }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, string? ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);
}
