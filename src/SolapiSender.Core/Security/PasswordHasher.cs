using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SolapiSender.Core.Security;

/// <summary>
/// Băm mật khẩu bằng PBKDF2-HMAC-SHA256 (OWASP 2023: ≥ 600.000 vòng cho SHA-256).
/// Định dạng lưu: "pbkdf2-sha256${iterations}${saltBase64}${hashBase64}".
/// </summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Prefix, iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>So sánh thời gian hằng định. Chuỗi sai định dạng → false.</summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Cần băm lại khi đăng nhập thành công (tăng số vòng theo thời gian).</summary>
    public static bool NeedsRehash(string stored) =>
        stored.Split('$') is [Prefix, var it, _, _] && int.TryParse(it, out var n) ? n < DefaultIterations : true;

    /// <summary>
    /// Chính sách mật khẩu admin: ≥ 10 ký tự, có chữ và số, khác tên đăng nhập và khác "admin".
    /// Trả về null nếu hợp lệ, hoặc lý do (tiếng Việt).
    /// </summary>
    public static string? ValidatePolicy(string password, string username)
    {
        if (password.Length < 10) return "Mật khẩu phải có ít nhất 10 ký tự.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Mật khẩu phải có cả chữ và số.";
        if (password.Equals(username, StringComparison.OrdinalIgnoreCase) ||
            password.Contains("admin", StringComparison.OrdinalIgnoreCase))
            return "Mật khẩu không được chứa tên đăng nhập hoặc chữ \"admin\".";
        return null;
    }
}
