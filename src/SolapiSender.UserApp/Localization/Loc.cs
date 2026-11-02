using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace SolapiSender.UserApp.Localization;

/// <summary>
/// Chuỗi giao diện từ Resources/Strings.resx (tiếng Hàn, mặc định) và Strings.vi.resx.
/// Đổi ngôn ngữ lúc chạy: mọi binding {l:Loc Key} tự cập nhật.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] SupportedLanguages = ["ko", "vi"];

    private static readonly ResourceManager Resources =
        new("SolapiSender.UserApp.Resources.Strings", typeof(Loc).Assembly);

    public static Loc Instance { get; } = new();

    public CultureInfo Culture { get; private set; } = new("ko-KR");

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string this[string key] => Resources.GetString(key, Culture) ?? $"[{key}]";

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    public void SetLanguage(string? code)
    {
        Culture = code == "vi" ? new CultureInfo("vi-VN") : new CultureInfo("ko-KR");
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        // Định dạng ngày/số hiển thị (DatePicker…). Dữ liệu gửi đi/lưu trữ luôn dùng InvariantCulture.
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Cách dùng trong XAML: Text="{l:Loc Login_Title}"</summary>
[MarkupExtensionReturnType(typeof(BindingExpression))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
