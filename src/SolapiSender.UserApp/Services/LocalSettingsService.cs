using System.IO;
using System.Text.Json;
using SolapiSender.Core.Security;

namespace SolapiSender.UserApp.Services;

/// <summary>Cài đặt chỉ thuộc về máy này (không đồng bộ lên Firestore, không chứa bí mật).</summary>
public sealed class LocalSettings
{
    /// <summary>"system" | "light" | "dark"</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"ko" | "vi"</summary>
    public string Language { get; set; } = "ko";

    public bool RememberLogin { get; set; } = true;
    public string? LastEmail { get; set; }
}

public sealed class LocalSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalSettings Settings { get; }

    public LocalSettingsService()
    {
        Settings = Load();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    private static LocalSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new LocalSettings();
    }
}
