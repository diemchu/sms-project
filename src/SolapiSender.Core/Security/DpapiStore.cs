using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SolapiSender.Core.Security;

/// <summary>
/// Lưu dữ liệu nhạy cảm (API Key/Secret, refresh token) mã hóa bằng Windows DPAPI
/// (phạm vi CurrentUser: chỉ tài khoản Windows hiện tại trên máy này giải mã được).
/// </summary>
public sealed class DpapiStore
{
    // Entropy phụ không phải bí mật; chỉ để tách dữ liệu của app này khỏi blob DPAPI khác.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SolapiSender.DpapiStore.v1");

    private readonly string _directory;

    public DpapiStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.SecureDirectory;
        Directory.CreateDirectory(_directory);
    }

    public void Save<T>(string name, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var path = PathOf(name);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Trả về default nếu chưa có hoặc không giải mã được (vd. copy sang máy/tài khoản khác).</summary>
    public T? Load<T>(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return default;

        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return default;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public bool Exists(string name) => File.Exists(PathOf(name));

    public void Delete(string name)
    {
        var path = PathOf(name);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathOf(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid store name", nameof(name));
        return Path.Combine(_directory, name + ".bin");
    }
}
