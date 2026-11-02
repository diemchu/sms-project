using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.UserApp.Services;

/// <summary>Dữ liệu Firestore của người dùng hiện tại: cài đặt đồng bộ và mẫu tin.</summary>
public sealed class CloudDataService
{
    private readonly FirestoreService _db;
    private readonly SessionService _session;

    public CloudDataService(FirestoreService db, SessionService session)
    {
        _db = db;
        _session = session;
    }

    private string UserPath => $"users/{_session.Uid}";

    public async Task<UserCloudSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.GetAsync<UserCloudSettings>($"{UserPath}/settings/app", ct) ?? new UserCloudSettings();
        _session.CloudSettings = s;
        return s;
    }

    public async Task SaveSettingsAsync(UserCloudSettings settings, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SetAsync($"{UserPath}/settings/app", settings, ct);
        _session.CloudSettings = settings;
    }

    public Task<List<MessageTemplate>> GetTemplatesAsync(CancellationToken ct = default) =>
        _db.ListAllAsync<MessageTemplate>($"{UserPath}/templates", "updatedAt desc", ct);

    public async Task<MessageTemplate> SaveTemplateAsync(MessageTemplate t, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        t.UpdatedAt = now;
        if (string.IsNullOrEmpty(t.Id))
        {
            t.CreatedAt = now;
            t.Id = await _db.CreateAsync($"{UserPath}/templates", t, ct: ct);
        }
        else
        {
            await _db.SetAsync($"{UserPath}/templates/{t.Id}", t, ct);
        }
        return t;
    }

    public Task DeleteTemplateAsync(string id, CancellationToken ct = default) =>
        _db.DeleteAsync($"{UserPath}/templates/{id}", ct);
}
