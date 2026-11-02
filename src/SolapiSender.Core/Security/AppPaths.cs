namespace SolapiSender.Core.Security;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolapiSender");

    public static string SecureDirectory => Path.Combine(Root, "secure");
    public static string LogDirectory => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
