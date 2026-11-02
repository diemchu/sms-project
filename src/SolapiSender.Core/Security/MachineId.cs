using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SolapiSender.Core.Security;

/// <summary>
/// ID máy ổn định, không lộ thông tin gốc: SHA-256(MachineGuid + tên app), lấy 32 ký tự hex.
/// MachineGuid đổi khi cài lại Windows → tính là thiết bị mới.
/// </summary>
public static class MachineId
{
    private static readonly Lazy<string> Value = new(Compute);

    public static string Current => Value.Value;

    public static string MachineName => Environment.MachineName;

    private static string Compute()
    {
        var guid = ReadMachineGuid() ?? Environment.MachineName + "|" + Environment.UserName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(guid + "|SolapiSender"));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }
}
