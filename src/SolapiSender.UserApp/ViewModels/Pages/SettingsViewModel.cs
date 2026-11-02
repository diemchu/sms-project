using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Security;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalSettingsService _local;
    private readonly SessionService _session;
    private readonly CloudDataService _cloud;
    private readonly CredentialStore _credentials;
    private readonly AppFlow _flow;
    private readonly UiService _ui;
    private bool _loading;

    public SettingsViewModel(LocalSettingsService local, SessionService session, CloudDataService cloud,
        CredentialStore credentials, AppFlow flow, UiService ui)
    {
        _local = local;
        _session = session;
        _cloud = cloud;
        _credentials = credentials;
        _flow = flow;
        _ui = ui;
        Reload();
    }

    [ObservableProperty] private string _theme = "system";
    [ObservableProperty] private string _language = "ko";

    [ObservableProperty] private double _lowBalanceThreshold;
    [ObservableProperty] private string _optOutNumber = "";
    [ObservableProperty] private double _priceSms;
    [ObservableProperty] private double _priceLms;
    [ObservableProperty] private double _priceMms;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _storeName = "";
    [ObservableProperty] private string _expiresAt = "";
    [ObservableProperty] private string _devices = "";
    [ObservableProperty] private string _maskedApiKey = "";

    public string Version => "v" + AppFlow.AppVersion;
    public string MachineIdShort => MachineId.Current[..8];

    public void Reload()
    {
        _loading = true;
        Theme = _local.Settings.Theme;
        Language = _local.Settings.Language;
        var c = _session.CloudSettings;
        LowBalanceThreshold = (double)c.LowBalanceThreshold;
        OptOutNumber = c.OptOutNumber ?? "";
        PriceSms = (double)c.PriceSms;
        PriceLms = (double)c.PriceLms;
        PriceMms = (double)c.PriceMms;

        var p = _session.Profile;
        Email = p?.Email ?? "";
        StoreName = p?.StoreName ?? "";
        ExpiresAt = p?.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
        Devices = p is null ? "" : $"{p.DeviceIds.Count} / {p.MaxDevices}";
        MaskedApiKey = _credentials.MaskedApiKey;
        _loading = false;
    }

    partial void OnThemeChanged(string value)
    {
        if (_loading) return;
        _local.Settings.Theme = value;
        _local.Save();
        UiService.ApplyTheme(value);
    }

    partial void OnLanguageChanged(string value)
    {
        if (_loading) return;
        _local.Settings.Language = value;
        _local.Save();
        Loc.Instance.SetLanguage(value);
    }

    [RelayCommand]
    private async Task SaveCloudAsync()
    {
        var s = _session.CloudSettings;
        s.LowBalanceThreshold = (decimal)Math.Max(0, LowBalanceThreshold);
        s.OptOutNumber = string.IsNullOrWhiteSpace(OptOutNumber) ? null : Core.PhoneNumberHelper.DigitsOnly(OptOutNumber);
        s.PriceSms = (decimal)Math.Max(0, PriceSms);
        s.PriceLms = (decimal)Math.Max(0, PriceLms);
        s.PriceMms = (decimal)Math.Max(0, PriceMms);
        try
        {
            await _cloud.SaveSettingsAsync(s);
            _ui.Success(Loc.T("Settings_Saved"));
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private void ChangeApiKey()
    {
        _flow.ShowConnect(isChange: true);
        MaskedApiKey = _credentials.MaskedApiKey;
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (await _ui.ConfirmAsync(Loc.T("Settings_Logout"), Loc.T("Settings_LogoutConfirm"), Loc.T("Settings_Logout")))
            await _flow.LogoutAsync();
    }

    [RelayCommand]
    private static void OpenLogs() =>
        Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
}
