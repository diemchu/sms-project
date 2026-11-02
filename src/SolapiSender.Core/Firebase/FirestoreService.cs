using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SolapiSender.Core.Firebase;

public sealed class FirestoreException(HttpStatusCode? statusCode, string? status, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>Mã gRPC dạng chuỗi: PERMISSION_DENIED, NOT_FOUND, FAILED_PRECONDITION, ALREADY_EXISTS…</summary>
    public string? Status { get; } = status;

    public bool IsPermissionDenied => Status == "PERMISSION_DENIED" || StatusCode == HttpStatusCode.Forbidden;
    public bool IsNetwork => StatusCode is null;
}

/// <summary>Một thao tác trong batch commit (nguyên tử, Security Rules đánh giá cả batch).</summary>
public sealed class FirestoreWrite
{
    private FirestoreWrite(Func<string, JsonObject> build) => Build = build;

    internal Func<string, JsonObject> Build { get; }

    /// <summary>Tạo document mới; thất bại nếu đã tồn tại.</summary>
    public static FirestoreWrite Create(string path, object data) => new(root => new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = $"{root}/{path}", ["fields"] = FirestoreMapper.ToFields(data) },
            ["currentDocument"] = new JsonObject { ["exists"] = false },
        });

    /// <summary>Cập nhật một số field của document đã tồn tại.</summary>
    public static FirestoreWrite Update(string path, IReadOnlyDictionary<string, object?> fields) => new(root => new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = $"{root}/{path}", ["fields"] = FirestoreMapper.ToFields(fields) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) },
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        });

    /// <summary>Thêm phần tử vào mảng nếu chưa có (arrayUnion, thực hiện phía server).</summary>
    public static FirestoreWrite ArrayUnion(string path, string field, params string[] values) => new(root => new JsonObject
        {
            ["transform"] = new JsonObject
            {
                ["document"] = $"{root}/{path}",
                ["fieldTransforms"] = new JsonArray(new JsonObject
                {
                    ["fieldPath"] = field,
                    ["appendMissingElements"] = new JsonObject
                    {
                        ["values"] = new JsonArray(values.Select(v => (JsonNode)FirestoreMapper.ToValue(v)).ToArray()),
                    },
                }),
            },
        });
}

/// <summary>Firestore REST API v1, xác thực bằng Firebase ID token của người dùng.</summary>
public sealed class FirestoreService
{
    private readonly HttpClient _http;
    private readonly FirebaseOptions _options;
    private readonly IIdTokenProvider _tokens;
    private readonly ILogger<FirestoreService> _logger;

    public FirestoreService(HttpClient http, FirebaseOptions options, IIdTokenProvider tokens, ILogger<FirestoreService>? logger = null)
    {
        _http = http;
        _options = options;
        _tokens = tokens;
        _logger = logger ?? NullLogger<FirestoreService>.Instance;
    }

    private string DocumentsRoot => $"projects/{_options.ProjectId}/databases/(default)/documents";
    private string BaseUrl => string.IsNullOrEmpty(_options.FirestoreEmulatorHost)
        ? $"https://firestore.googleapis.com/v1/{DocumentsRoot}"
        : $"http://{_options.FirestoreEmulatorHost}/v1/{DocumentsRoot}";

    /// <summary>Đọc document, trả null nếu không tồn tại.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default) where T : class, new()
    {
        using var doc = await SendAsync(HttpMethod.Get, $"{BaseUrl}/{path}", null, allowNotFound: true, ct);
        return doc is null ? null : FirestoreMapper.FromDocument<T>(doc.RootElement);
    }

    public async Task<(List<T> Items, string? NextPageToken)> ListAsync<T>(string collectionPath, int pageSize = 100,
        string? orderBy = null, string? pageToken = null, CancellationToken ct = default) where T : new()
    {
        var url = new StringBuilder($"{BaseUrl}/{collectionPath}?pageSize={pageSize}");
        if (orderBy is not null) url.Append("&orderBy=").Append(Uri.EscapeDataString(orderBy));
        if (pageToken is not null) url.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

        using var doc = await SendAsync(HttpMethod.Get, url.ToString(), null, allowNotFound: false, ct);
        var items = new List<T>();
        string? next = null;
        if (doc is not null)
        {
            if (doc.RootElement.TryGetProperty("documents", out var docs))
                items.AddRange(docs.EnumerateArray().Select(FirestoreMapper.FromDocument<T>));
            if (doc.RootElement.TryGetProperty("nextPageToken", out var t)) next = t.GetString();
        }
        return (items, next);
    }

    /// <summary>Đọc toàn bộ collection (tự phân trang).</summary>
    public async Task<List<T>> ListAllAsync<T>(string collectionPath, string? orderBy = null, CancellationToken ct = default) where T : new()
    {
        var all = new List<T>();
        string? token = null;
        do
        {
            var (items, next) = await ListAsync<T>(collectionPath, 300, orderBy, token, ct);
            all.AddRange(items);
            token = next;
        } while (!string.IsNullOrEmpty(token));
        return all;
    }

    /// <summary>Tạo document trong collection; <paramref name="documentId"/> null → Firestore tự sinh. Trả về ID.</summary>
    public async Task<string> CreateAsync(string collectionPath, object data, string? documentId = null, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{collectionPath}" + (documentId is null ? "" : $"?documentId={Uri.EscapeDataString(documentId)}");
        var body = new JsonObject { ["fields"] = FirestoreMapper.ToFields(data) };
        using var doc = await SendAsync(HttpMethod.Post, url, body, allowNotFound: false, ct);
        return doc!.RootElement.GetProperty("name").GetString()!.Split('/').Last();
    }

    /// <summary>Ghi đè toàn bộ document (tạo mới nếu chưa có).</summary>
    public async Task SetAsync(string path, object data, CancellationToken ct = default)
    {
        var body = new JsonObject { ["fields"] = FirestoreMapper.ToFields(data) };
        using var _ = await SendAsync(HttpMethod.Patch, $"{BaseUrl}/{path}", body, allowNotFound: false, ct);
    }

    /// <summary>Cập nhật một số field; document phải tồn tại.</summary>
    public async Task UpdateAsync(string path, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default)
    {
        var url = new StringBuilder($"{BaseUrl}/{path}?currentDocument.exists=true");
        foreach (var key in fields.Keys) url.Append("&updateMask.fieldPaths=").Append(Uri.EscapeDataString(key));
        var body = new JsonObject { ["fields"] = FirestoreMapper.ToFields(fields) };
        using var _ = await SendAsync(HttpMethod.Patch, url.ToString(), body, allowNotFound: false, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"{BaseUrl}/{path}", null, allowNotFound: true, ct);
    }

    /// <summary>Ghi nhiều thao tác một cách nguyên tử.</summary>
    public async Task CommitAsync(IEnumerable<FirestoreWrite> writes, CancellationToken ct = default)
    {
        var array = new JsonArray(writes.Select(w => (JsonNode)w.Build(DocumentsRoot)).ToArray());
        var body = new JsonObject { ["writes"] = array };
        using var _ = await SendAsync(HttpMethod.Post, $"{BaseUrl}:commit", body, allowNotFound: false, ct);
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string url, JsonNode? body, bool allowNotFound, CancellationToken ct)
    {
        var token = await _tokens.GetIdTokenAsync(ct);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Firestore {Method} lỗi mạng", method);
            throw new FirestoreException(null, null, ex.Message, ex);
        }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == HttpStatusCode.NotFound && allowNotFound) return null;
            if (res.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);

            string? status = null, message = null;
            try
            {
                using var err = JsonDocument.Parse(text);
                var e = err.RootElement.GetProperty("error");
                status = e.TryGetProperty("status", out var s) ? s.GetString() : null;
                message = e.TryGetProperty("message", out var m) ? m.GetString() : null;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
            }

            _logger.LogWarning("Firestore {Method} {Path} → {Status} {GrpcStatus}: {Message}",
                method, PathForLog(url), (int)res.StatusCode, status, message);
            throw new FirestoreException(res.StatusCode, status, message ?? $"HTTP {(int)res.StatusCode}");
        }
    }

    private string PathForLog(string url) => url.Replace(BaseUrl, "").Split('?')[0];
}
