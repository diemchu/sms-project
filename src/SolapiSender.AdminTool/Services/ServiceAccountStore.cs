using System.IO;
using System.Text.Json;
using SolapiSender.Core.Security;

namespace SolapiSender.AdminTool.Services;

/// <summary>
/// Lưu nội dung file service account JSON đã mã hóa DPAPI trên máy dev
/// (sau khi nạp có thể cất/xóa file gốc). Không bao giờ nằm trong UserApp.
/// </summary>
public sealed class ServiceAccountStore
{
    private const string StoreName = "admin-service-account";
    private readonly DpapiStore _store;

    public ServiceAccountStore(DpapiStore store) => _store = store;

    public string? Load() => _store.Load<string>(StoreName);

    public void Save(string json) => _store.Save(StoreName, json);

    public void Clear() => _store.Delete(StoreName);

    /// <summary>Đọc file và kiểm tra đúng là service account của Google. Ném <see cref="AdminException"/> nếu sai.</summary>
    public static (string Json, ServiceAccountInfo Info) ReadAndValidate(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AdminException($"Không đọc được file: {ex.Message}", ex);
        }
        return (json, Parse(json));
    }

    public static ServiceAccountInfo Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Get(string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

            if (Get("type") != "service_account")
                throw new AdminException("File không phải service account (thiếu \"type\": \"service_account\").");
            if (Get("private_key").Length == 0 || Get("client_email").Length == 0 || Get("project_id").Length == 0)
                throw new AdminException("File service account thiếu private_key / client_email / project_id.");
            return new ServiceAccountInfo(Get("project_id"), Get("client_email"));
        }
        catch (JsonException ex)
        {
            throw new AdminException("File không phải JSON hợp lệ.", ex);
        }
    }
}
