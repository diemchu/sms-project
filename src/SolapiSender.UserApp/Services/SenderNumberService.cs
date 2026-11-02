using System.Collections.ObjectModel;
using SolapiSender.Core;
using SolapiSender.Core.Solapi;

namespace SolapiSender.UserApp.Services;

/// <summary>Danh sách số gửi đã đăng ký trên SOLAPI (dùng chung cho trang Số gửi và Gửi tin).</summary>
public sealed class SenderNumberService
{
    private readonly SolapiClient _solapi;
    private readonly CloudDataService _cloud;
    private readonly SessionService _session;

    public SenderNumberService(SolapiClient solapi, CloudDataService cloud, SessionService session)
    {
        _solapi = solapi;
        _cloud = cloud;
        _session = session;
    }

    public ObservableCollection<string> Numbers { get; } = [];

    public bool Loaded { get; private set; }

    public string? DefaultNumber => _session.CloudSettings.DefaultSender is { } d && Numbers.Contains(d) ? d : Numbers.FirstOrDefault();

    public event EventHandler? DefaultChanged;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var list = await _solapi.GetActiveSenderNumbersAsync(ct);
        Numbers.Clear();
        foreach (var n in list) Numbers.Add(n);
        Loaded = true;
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (!Loaded) await RefreshAsync(ct);
    }

    public async Task SetDefaultAsync(string number)
    {
        var s = _session.CloudSettings;
        s.DefaultSender = number;
        await _cloud.SaveSettingsAsync(s);
        DefaultChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string Display(string number)
    {
        if (PhoneNumberHelper.IsValidMobile(number)) return PhoneNumberHelper.Format(number);
        return number switch
        {
            { Length: 9 } when number.StartsWith("02") => $"{number[..2]}-{number[2..5]}-{number[5..]}",
            { Length: 10 } when number.StartsWith("02") => $"{number[..2]}-{number[2..6]}-{number[6..]}",
            { Length: 8 } when number.StartsWith('1') => $"{number[..4]}-{number[4..]}",
            { Length: 10 } => $"{number[..3]}-{number[3..6]}-{number[6..]}",
            { Length: 11 } => $"{number[..3]}-{number[3..7]}-{number[7..]}",
            _ => number,
        };
    }
}
