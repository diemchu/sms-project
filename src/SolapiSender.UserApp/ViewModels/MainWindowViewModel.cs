using CommunityToolkit.Mvvm.ComponentModel;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SessionService _session;

    public MainWindowViewModel(SessionService session)
    {
        _session = session;
        _session.Changed += (_, _) => Refresh();
        Loc.Instance.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty] private string _storeName = "";
    [ObservableProperty] private string _expiryText = "";
    [ObservableProperty] private bool _expirySoon;

    public string Title => App.PreviewMode ? "SOLAPI Sender — PREVIEW" : "SOLAPI Sender";

    private void Refresh()
    {
        var p = _session.Profile;
        if (p is null) return;
        StoreName = string.IsNullOrWhiteSpace(p.StoreName) ? p.Email : p.StoreName;
        var days = (int)Math.Ceiling((p.ExpiresAt - DateTimeOffset.UtcNow).TotalDays);
        ExpiryText = Loc.F("Main_ExpiresIn", p.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd"), Math.Max(0, days));
        ExpirySoon = days <= 7;
    }
}
