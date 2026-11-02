using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SolapiSender.Core.Solapi;

/// <summary>
/// Header xác thực SOLAPI (đối chiếu solapi-nodejs/src/lib/authenticator.ts):
/// "HMAC-SHA256 apiKey={key}, date={ISO8601}, salt={32 ký tự ngẫu nhiên}, signature={hex(HMAC_SHA256(secret, date + salt))}"
/// </summary>
public static class SolapiSigner
{
    public const int SaltLength = 32;

    public static string CreateAuthorization(string apiKey, string apiSecret, DateTimeOffset now, string salt)
    {
        var date = FormatDate(now);
        var signature = ComputeSignature(apiSecret, date, salt);
        return $"HMAC-SHA256 apiKey={apiKey}, date={date}, salt={salt}, signature={signature}";
    }

    public static string CreateAuthorization(string apiKey, string apiSecret) =>
        CreateAuthorization(apiKey, apiSecret, DateTimeOffset.UtcNow, CreateSalt());

    public static string ComputeSignature(string apiSecret, string date, string salt)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiSecret), Encoding.UTF8.GetBytes(date + salt));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>32 ký tự hex ngẫu nhiên (an toàn mật mã).</summary>
    public static string CreateSalt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltLength / 2)).ToLowerInvariant();

    public static string FormatDate(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
