using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Security;
using SolapiSender.Core.Solapi;

namespace SolapiSender.UserApp.Services;

/// <summary>Trạng thái người dùng đang đăng nhập + cài đặt đồng bộ của họ.</summary>
public sealed class SessionService
{
    private const string RefreshTokenStore = "session";

    private readonly DpapiStore _store;

    public SessionService(DpapiStore store) => _store = store;

    public UserProfile? Profile { get; private set; }
    public UserCloudSettings CloudSettings { get; set; } = new();

    public string Uid => Profile?.Uid ?? "";
    public string MachineId => Core.Security.MachineId.Current;
    public bool IsActive => Profile is not null;

    public event EventHandler? Changed;

    public void Start(UserProfile profile)
    {
        Profile = profile;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateProfile(UserProfile profile)
    {
        Profile = profile;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void End()
    {
        Profile = null;
        CloudSettings = new UserCloudSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveRefreshToken(string token) => _store.Save(RefreshTokenStore, token);
    public string? LoadRefreshToken() => _store.Load<string>(RefreshTokenStore);
    public void ClearRefreshToken() => _store.Delete(RefreshTokenStore);
}

/// <summary>API Key/Secret SOLAPI của từng tài khoản app, mã hóa DPAPI trên máy.</summary>
public sealed class CredentialStore : ISolapiCredentialProvider
{
    private readonly DpapiStore _store;
    private readonly SessionService _session;
    private SolapiCredentials? _cache;
    private string? _cacheUid;

    public CredentialStore(DpapiStore store, SessionService session)
    {
        _store = store;
        _session = session;
    }

    private string Name => "solapi-" + _session.Uid;

    public SolapiCredentials? GetCredentials()
    {
        if (!_session.IsActive) return null;
        if (_cacheUid != _session.Uid)
        {
            _cache = _store.Load<SolapiCredentials>(Name);
            _cacheUid = _session.Uid;
        }
        return _cache;
    }

    public bool HasCredentials => GetCredentials() is not null;

    public void Save(SolapiCredentials credentials)
    {
        _store.Save(Name, credentials);
        _cache = credentials;
        _cacheUid = _session.Uid;
    }

    public void Clear()
    {
        _store.Delete(Name);
        _cache = null;
    }

    /// <summary>Hiển thị dạng "NCS1****ABCD" để người dùng nhận ra key đang dùng.</summary>
    public string MaskedApiKey
    {
        get
        {
            var key = GetCredentials()?.ApiKey;
            if (string.IsNullOrEmpty(key)) return "-";
            return key.Length <= 8 ? new string('*', key.Length) : key[..4] + new string('*', key.Length - 8) + key[^4..];
        }
    }
}
