using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SolapiSender.AdminTool.Preview;
using SolapiSender.AdminTool.Services;
using SolapiSender.AdminTool.ViewModels;
using SolapiSender.AdminTool.Views;
using SolapiSender.AdminTool.Views.Pages;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Logging;
using SolapiSender.Core.Security;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace SolapiSender.AdminTool;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>Xem trước giao diện với dữ liệu giả lập (chỉ bản Debug, tham số --preview).</summary>
    public static bool PreviewMode { get; private set; }

    public App()
    {
        // Định dạng ngày theo tiếng Việt (đặt đồng bộ, không trong hàm async).
        var culture = new CultureInfo("vi-VN");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        PreviewMode = e.Args.Contains("--preview");
#endif
        DispatcherUnhandledException += OnUnhandledException;

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(l =>
            {
                l.ClearProviders();
                l.AddProvider(new FileLoggerProvider(System.IO.Path.Combine(AppPaths.Root, "admin-logs")));
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        Resources["AppFont"] = new FontFamily(new Uri("pack://application:,,,/"),
            "./Assets/Fonts/#Pretendard, Segoe UI Variable Text, Malgun Gothic, Segoe UI");
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica, true);

        _host.Services.GetRequiredService<AdminFlow>().Start();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_ => new DpapiStore());
        services.AddSingleton<ServiceAccountStore>();
        services.AddSingleton<FirebaseAdminContext>();
        services.AddSingleton<ExcelService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<UiService>();
        services.AddSingleton<AdminFlow>();

        if (PreviewMode)
        {
            services.AddSingleton<InMemoryAdminBackend>();
            services.AddSingleton<IAdminAuthService>(sp => sp.GetRequiredService<InMemoryAdminBackend>());
            services.AddSingleton<IUserAdminService>(sp => sp.GetRequiredService<InMemoryAdminBackend>());
        }
        else
        {
            services.AddSingleton<IAdminAuthService, FirebaseAdminAuthService>();
            services.AddSingleton<IUserAdminService, FirebaseUserAdminService>();
        }

        services.AddTransient<SetupViewModel>();
        services.AddTransient<SetupWindow>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ChangePasswordViewModel>();
        services.AddTransient<ChangePasswordWindow>();
        services.AddTransient<MainWindow>();

        // Trang giữ trạng thái trong suốt phiên của cửa sổ Main.
        services.AddSingleton<UsersViewModel>();
        services.AddTransient<UsersPage>();
        services.AddSingleton<LogsViewModel>();
        services.AddTransient<LogsPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<UserEditViewModel>();
        services.AddTransient<UserEditWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Services.GetService<FirebaseAdminContext>()?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "Lỗi không xử lý");
        var ui = _host?.Services.GetService<UiService>();
        if (ui is null) return;
        ui.Error(e.Exception);
        e.Handled = true;
    }
}
