using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SolapiSender.Core.Firebase;

/// <summary>Cấu hình công khai của Firebase Web app (không phải secret — bảo vệ bằng Security Rules).</summary>
public sealed class FirebaseOptions
{
    public string WebApiKey { get; set; } = "";
    public string ProjectId { get; set; } = "";

    /// <summary>"localhost:8085" để dùng Firestore Emulator khi phát triển/kiểm thử. Để trống khi chạy thật.</summary>
    public string? FirestoreEmulatorHost { get; set; }

    /// <summary>"localhost:9099" để dùng Firebase Auth Emulator. Để trống khi chạy thật.</summary>
    public string? AuthEmulatorHost { get; set; }
}

public sealed record FirebaseSession(string Uid, string Email, string IdToken, string RefreshToken, DateTimeOffset ExpiresAt);

public enum FirebaseAuthErrorKind
{
    Unknown,
    Network,
    InvalidCredentials,
    UserDisabled,
    TooManyAttempts,
    SessionExpired,
    NotConfigured,
}

public sealed class FirebaseAuthException(FirebaseAuthErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public FirebaseAuthErrorKind Kind { get; } = kind;
}

public interface IIdTokenProvider
{
    Task<string> GetIdTokenAsync(CancellationToken ct = default);
}

/// <summary>Firebase Authentication qua REST API (Identity Toolkit + Secure Token).</summary>
public sealed class FirebaseAuthService : IIdTokenProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly FirebaseOptions _options;
    private readonly ILogger<FirebaseAuthService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public FirebaseAuthService(HttpClient http, FirebaseOptions options, ILogger<FirebaseAuthService>? logger = null)
    {
        _http = http;
        _options = options;
        _logger = logger ?? NullLogger<FirebaseAuthService>.Instance;
    }

    public FirebaseSession? Session { get; private set; }

    public event EventHandler? SessionChanged;

    public async Task<FirebaseSession> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        EnsureConfigured();
        var url = $"{AuthBase("identitytoolkit")}/v1/accounts:signInWithPassword?key={_options.WebApiKey}";
        var body = new { email = email.Trim(), password, returnSecureToken = true };

        var res = await PostAsync<SignInResponse>(url, JsonContent.Create(body), ct);
        SetSession(new FirebaseSession(res.LocalId, res.Email ?? email, res.IdToken, res.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(ParseSeconds(res.ExpiresIn))));
        return Session!;
    }

    /// <summary>Khôi phục phiên từ refresh token đã lưu (ghi nhớ đăng nhập).</summary>
    public async Task<FirebaseSession> RestoreAsync(string refreshToken, CancellationToken ct = default)
    {
        EnsureConfigured();
        await RefreshCoreAsync(refreshToken, email: null, ct);
        return Session!;
    }

    public async Task<string> GetIdTokenAsync(CancellationToken ct = default)
    {
        var s = Session ?? throw new FirebaseAuthException(FirebaseAuthErrorKind.SessionExpired, "Chưa đăng nhập.");
        if (s.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin) return s.IdToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            s = Session ?? throw new FirebaseAuthException(FirebaseAuthErrorKind.SessionExpired, "Chưa đăng nhập.");
            if (s.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin) return s.IdToken;
            await RefreshCoreAsync(s.RefreshToken, s.Email, ct);
            return Session!.IdToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void SignOut() => SetSession(null);

    private async Task RefreshCoreAsync(string refreshToken, string? email, CancellationToken ct)
    {
        var url = $"{AuthBase("securetoken")}/v1/token?key={_options.WebApiKey}";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        RefreshResponse res;
        try
        {
            res = await PostAsync<RefreshResponse>(url, content, ct);
        }
        catch (FirebaseAuthException ex) when (ex.Kind is not FirebaseAuthErrorKind.Network)
        {
            // Token bị thu hồi (admin khóa user) hoặc user bị disable.
            SetSession(null);
            throw new FirebaseAuthException(
                ex.Kind == FirebaseAuthErrorKind.UserDisabled ? ex.Kind : FirebaseAuthErrorKind.SessionExpired, ex.Message, ex);
        }

        var claimsEmail = email ?? TryReadEmailClaim(res.IdToken) ?? "";
        SetSession(new FirebaseSession(res.UserId, claimsEmail, res.IdToken, res.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(ParseSeconds(res.ExpiresIn))));
    }

    private async Task<T> PostAsync<T>(string url, HttpContent content, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.PostAsync(url, content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Firebase Auth lỗi mạng");
            throw new FirebaseAuthException(FirebaseAuthErrorKind.Network, ex.Message, ex);
        }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<T>(text)
                       ?? throw new FirebaseAuthException(FirebaseAuthErrorKind.Unknown, "Empty response");

            var code = TryReadErrorCode(text) ?? $"HTTP_{(int)res.StatusCode}";
            _logger.LogWarning("Firebase Auth lỗi {Code}", code);
            throw new FirebaseAuthException(MapError(code), code);
        }
    }

    internal static FirebaseAuthErrorKind MapError(string code)
    {
        // Thông báo có thể kèm mô tả: "TOO_MANY_ATTEMPTS_TRY_LATER : ..."
        var key = code.Split(' ', ':')[0];
        return key switch
        {
            "INVALID_LOGIN_CREDENTIALS" or "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_EMAIL"
                or "MISSING_PASSWORD" => FirebaseAuthErrorKind.InvalidCredentials,
            "USER_DISABLED" => FirebaseAuthErrorKind.UserDisabled,
            "TOO_MANY_ATTEMPTS_TRY_LATER" => FirebaseAuthErrorKind.TooManyAttempts,
            "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "USER_NOT_FOUND" or "INVALID_ID_TOKEN" => FirebaseAuthErrorKind.SessionExpired,
            "API_KEY_INVALID" or "CONFIGURATION_NOT_FOUND" or "PROJECT_NOT_FOUND" => FirebaseAuthErrorKind.NotConfigured,
            _ => FirebaseAuthErrorKind.Unknown,
        };
    }

    private static string? TryReadErrorCode(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryReadEmailClaim(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Gốc URL của API Auth; với emulator: http://host/{service}.googleapis.com.</summary>
    private string AuthBase(string service) => string.IsNullOrEmpty(_options.AuthEmulatorHost)
        ? $"https://{service}.googleapis.com"
        : $"http://{_options.AuthEmulatorHost}/{service}.googleapis.com";

    private static double ParseSeconds(string? s) => double.TryParse(s, out var v) ? v : 3600;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.WebApiKey) || string.IsNullOrWhiteSpace(_options.ProjectId))
            throw new FirebaseAuthException(FirebaseAuthErrorKind.NotConfigured, "Firebase chưa được cấu hình (appsettings.json).");
    }

    private void SetSession(FirebaseSession? session)
    {
        Session = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SignInResponse
    {
        [JsonPropertyName("localId")] public string LocalId { get; set; } = "";
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("idToken")] public string IdToken { get; set; } = "";
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expiresIn")] public string? ExpiresIn { get; set; }
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
        [JsonPropertyName("id_token")] public string IdToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public string? ExpiresIn { get; set; }
    }
}
