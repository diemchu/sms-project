using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolapiSender.Core.Models.Solapi;

namespace SolapiSender.Core.Solapi;

/// <summary>Nguồn API Key/Secret hiện tại (UserApp đọc từ kho DPAPI).</summary>
public interface ISolapiCredentialProvider
{
    SolapiCredentials? GetCredentials();
}

public sealed class SolapiClient
{
    public const string DefaultBaseUrl = "https://api.solapi.com/";
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly ISolapiCredentialProvider _credentials;
    private readonly ILogger<SolapiClient> _logger;

    public SolapiClient(HttpClient http, ISolapiCredentialProvider credentials, ILogger<SolapiClient>? logger = null)
    {
        _http = http;
        _http.BaseAddress ??= new Uri(DefaultBaseUrl);
        _credentials = credentials;
        _logger = logger ?? NullLogger<SolapiClient>.Instance;
    }

    // ------------------------------------------------------------------ Cash

    public Task<BalanceResponse> GetBalanceAsync(CancellationToken ct = default) =>
        SendAsync<BalanceResponse>(HttpMethod.Get, "cash/v1/balance", null, RetryMode.Idempotent, null, ct);

    /// <summary>Kiểm tra key/secret mới bằng cách gọi số dư trước khi lưu.</summary>
    public Task<BalanceResponse> VerifyCredentialsAsync(SolapiCredentials credentials, CancellationToken ct = default) =>
        SendAsync<BalanceResponse>(HttpMethod.Get, "cash/v1/balance", null, RetryMode.Idempotent, credentials, ct);

    // ------------------------------------------------------------------ Sender ID

    /// <summary>GET /senderid/v1/numbers/active — các số gửi đã đăng ký và đang hoạt động.</summary>
    public async Task<IReadOnlyList<string>> GetActiveSenderNumbersAsync(CancellationToken ct = default)
    {
        var json = await SendAsync<JsonElement>(HttpMethod.Get, "senderid/v1/numbers/active", null, RetryMode.Idempotent, null, ct);
        return ParseSenderNumbers(json);
    }

    /// <summary>
    /// Server trả về mảng chuỗi số điện thoại. Để an toàn, cũng chấp nhận mảng object có "phoneNumber"
    /// hoặc object bọc mảng.
    /// </summary>
    internal static IReadOnlyList<string> ParseSenderNumbers(JsonElement json)
    {
        var result = new List<string>();

        void Collect(JsonElement array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var value = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("phoneNumber", out var p) => p.GetString(),
                    _ => null,
                };
                var digits = PhoneNumberHelper.DigitsOnly(value);
                if (digits.Length > 0 && !result.Contains(digits)) result.Add(digits);
            }
        }

        if (json.ValueKind == JsonValueKind.Array) Collect(json);
        else if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in json.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Array) Collect(prop.Value);
        }
        return result;
    }

    // ------------------------------------------------------------------ Storage

    /// <summary>Tải ảnh JPG (≤200KB) lên để gửi MMS, trả về imageId.</summary>
    public async Task<string> UploadMmsImageAsync(byte[] jpegBytes, string fileName, CancellationToken ct = default)
    {
        var body = new FileUploadRequest { File = Convert.ToBase64String(jpegBytes), Type = "MMS", Name = fileName };
        var res = await SendAsync<FileUploadResponse>(HttpMethod.Post, "storage/v1/files", body, RetryMode.Idempotent, null, ct);
        return res.FileId;
    }

    // ------------------------------------------------------------------ Messages

    /// <summary>
    /// POST /messages/v4/send-many/detail (tối đa 10.000 tin/lần).
    /// Không tự retry khi lỗi mạng/5xx vì có thể server đã nhận và sẽ gửi trùng; chỉ retry khi 429.
    /// </summary>
    public Task<SendManyResponse> SendManyDetailAsync(SendManyRequest request, CancellationToken ct = default) =>
        SendAsync<SendManyResponse>(HttpMethod.Post, "messages/v4/send-many/detail", request, RetryMode.OnlyRateLimit, null, ct);

    public Task<MessageListResponse> GetMessagesAsync(MessageListQuery q, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("startDate", q.StartDate)
            .Add("endDate", q.EndDate)
            .Add("groupId", q.GroupId)
            .Add("to", q.To)
            .Add("from", q.From)
            .Add("type", q.Type)
            .Add("statusCode", q.StatusCode)
            .Add("limit", q.Limit.ToString())
            .Add("startKey", q.StartKey);
        return SendAsync<MessageListResponse>(HttpMethod.Get, "messages/v4/list" + query, null, RetryMode.Idempotent, null, ct);
    }

    public Task<GroupListResponse> GetGroupsAsync(GroupListQuery q, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("startDate", q.StartDate)
            .Add("endDate", q.EndDate)
            .Add("limit", q.Limit.ToString())
            .Add("startKey", q.StartKey);
        return SendAsync<GroupListResponse>(HttpMethod.Get, "messages/v4/groups" + query, null, RetryMode.Idempotent, null, ct);
    }

    public Task<StatisticsResponse> GetStatisticsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var query = new QueryBuilder().Add("startDate", start).Add("endDate", end);
        return SendAsync<StatisticsResponse>(HttpMethod.Get, "messages/v4/statistics" + query, null, RetryMode.Idempotent, null, ct);
    }

    // ------------------------------------------------------------------ Core HTTP

    private enum RetryMode
    {
        Idempotent,
        OnlyRateLimit,
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, RetryMode retry,
        SolapiCredentials? overrideCredentials, CancellationToken ct)
    {
        var creds = overrideCredentials ?? _credentials.GetCredentials()
            ?? throw new SolapiException(SolapiErrorKind.InvalidCredentials, "SOLAPI API key chưa được cấu hình.");

        for (var attempt = 1; ; attempt++)
        {
            // Mỗi lần thử phải ký lại (date + salt mới).
            using var req = new HttpRequestMessage(method, path);
            req.Headers.TryAddWithoutValidation("Authorization", SolapiSigner.CreateAuthorization(creds.ApiKey, creds.ApiSecret));
            if (body is not null)
                req.Content = JsonContent.Create(body, body.GetType(), options: SolapiJson.Options);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                var kind = ex is TaskCanceledException ? SolapiErrorKind.Timeout : SolapiErrorKind.Network;
                if (retry == RetryMode.Idempotent && attempt < MaxAttempts)
                {
                    _logger.LogWarning(ex, "SOLAPI {Method} {Path} lỗi mạng, thử lại lần {Attempt}", method, StripQuery(path), attempt + 1);
                    await Task.Delay(Backoff(attempt), ct);
                    continue;
                }
                _logger.LogError(ex, "SOLAPI {Method} {Path} lỗi mạng", method, StripQuery(path));
                throw new SolapiException(kind, ex.Message, inner: ex);
            }

            using (res)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                if (res.IsSuccessStatusCode)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<T>(text, SolapiJson.Options)
                            ?? throw new JsonException("Empty response");
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "SOLAPI {Path}: không đọc được phản hồi", StripQuery(path));
                        throw new SolapiException(SolapiErrorKind.Unknown, "Phản hồi SOLAPI không hợp lệ.", res.StatusCode, inner: ex);
                    }
                }

                var error = TryParseError(text);
                var kind = SolapiErrorClassifier.Classify(res.StatusCode, error?.ErrorCode, error?.ErrorMessage);
                var retryable = kind == SolapiErrorKind.RateLimited
                    || (retry == RetryMode.Idempotent && kind == SolapiErrorKind.Server);

                if (retryable && attempt < MaxAttempts)
                {
                    _logger.LogWarning("SOLAPI {Method} {Path} → {Status} {Code}, thử lại lần {Attempt}",
                        method, StripQuery(path), (int)res.StatusCode, error?.ErrorCode, attempt + 1);
                    await Task.Delay(RetryAfter(res) ?? Backoff(attempt), ct);
                    continue;
                }

                _logger.LogError("SOLAPI {Method} {Path} → {Status} {Code}: {Message}",
                    method, StripQuery(path), (int)res.StatusCode, error?.ErrorCode, error?.ErrorMessage);
                throw new SolapiException(kind, error?.ErrorMessage ?? $"HTTP {(int)res.StatusCode}",
                    res.StatusCode, error?.ErrorCode, error?.ErrorMessage);
            }
        }
    }

    private static SolapiErrorBody? TryParseError(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<SolapiErrorBody>(text, SolapiJson.Options); }
        catch (JsonException) { return null; }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    private static TimeSpan? RetryAfter(HttpResponseMessage res)
    {
        var delta = res.Headers.RetryAfter?.Delta;
        return delta is { } d && d < TimeSpan.FromSeconds(30) ? d : null;
    }

    private static string StripQuery(string path) => path.Split('?')[0];

    private sealed class QueryBuilder
    {
        private readonly StringBuilder _sb = new();

        public QueryBuilder Add(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return this;
            _sb.Append(_sb.Length == 0 ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
            return this;
        }

        public QueryBuilder Add(string key, DateTimeOffset? value) =>
            value is null ? this : Add(key, SolapiSigner.FormatDate(value.Value));

        public override string ToString() => _sb.ToString();
    }
}
