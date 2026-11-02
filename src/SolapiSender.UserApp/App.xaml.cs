using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Logging;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Security;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Preview;
using SolapiSender.UserApp.Services;
using SolapiSender.UserApp.ViewModels;
using SolapiSender.UserApp.ViewModels.Pages;
using SolapiSender.UserApp.Views;
using SolapiSender.UserApp.Views.Pages;
using Wpf.Ui;

namespace SolapiSender.UserApp;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>Chế độ xem trước giao diện với dữ liệu mẫu (chỉ bản Debug, tham số --preview).</summary>
    public static bool PreviewMode { get; private set; }

    public App()
    {
        // Đặt ngôn ngữ ở đây (đồng bộ) chứ không trong OnStartup async: thay đổi CurrentCulture
        // bên trong hàm async bị hoàn lại khi hàm kết thúc.
        Loc.Instance.SetLanguage(new LocalSettingsService().Settings.Language);
        // WPF mặc định định dạng theo en-US; dùng ngôn ngữ của app (áp dụng từ lần khởi động này).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag)));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        PreviewMode = e.Args.Contains("--preview");
#endif
        DispatcherUnhandledException += OnUnhandledException;

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new FileLoggerProvider());
            })
            .ConfigureServices((ctx, services) => ConfigureServices(ctx.Configuration, services))
            .Build();
        await _host.StartAsync();

        Resources["AppFont"] = new FontFamily(new Uri("pack://application:,,,/"),
            "./Assets/Fonts/#Pretendard, Segoe UI Variable Text, Malgun Gothic, Segoe UI");

        UiService.ApplyTheme(_host.Services.GetRequiredService<LocalSettingsService>().Settings.Theme);

        await _host.Services.GetRequiredService<AppFlow>().StartAsync();
    }

    private static void ConfigureServices(IConfiguration config, IServiceCollection services)
    {
        var firebase = config.GetSection("Firebase").Get<FirebaseOptions>() ?? new FirebaseOptions();
        if (PreviewMode) firebase = new FirebaseOptions { WebApiKey = "preview", ProjectId = "preview" };
        services.AddSingleton(firebase);

        HttpMessageHandler NewHandler() => PreviewMode
            ? new PreviewHttpHandler()
            : new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };

        var firebaseHttp = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        // Gửi lô 10.000 tin có thể mất vài phút.
        var solapiHttp = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromMinutes(5), BaseAddress = new Uri(SolapiClient.DefaultBaseUrl) };

        // Hạ tầng dùng chung toàn app
        services.AddSingleton<LocalSettingsService>();
        services.AddSingleton(_ => new DpapiStore());
        services.AddSingleton<SessionService>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<ISolapiCredentialProvider>(sp => sp.GetRequiredService<CredentialStore>());
        services.AddSingleton(sp => new FirebaseAuthService(firebaseHttp, firebase, sp.GetRequiredService<ILogger<FirebaseAuthService>>()));
        services.AddSingleton<IIdTokenProvider>(sp => PreviewMode
            ? new PreviewTokenProvider()
            : sp.GetRequiredService<FirebaseAuthService>());
        services.AddSingleton(sp => new FirestoreService(firebaseHttp, firebase, sp.GetRequiredService<IIdTokenProvider>(),
            sp.GetRequiredService<ILogger<FirestoreService>>()));
        services.AddSingleton<LicenseService>();
        services.AddSingleton<ActivityLogService>();
        services.AddSingleton(sp => new SolapiClient(solapiHttp, sp.GetRequiredService<ISolapiCredentialProvider>(),
            sp.GetRequiredService<ILogger<SolapiClient>>()));
        services.AddSingleton<BulkSender>();
        services.AddSingleton<ExcelService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<UiService>();
        services.AddSingleton<AppFlow>();

        // Trước đăng nhập
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();

        // Theo phiên đăng nhập (scope)
        services.AddScoped<CloudDataService>();
        services.AddScoped<SenderNumberService>();
        services.AddScoped<ConnectSolapiViewModel>();
        services.AddTransient<ConnectSolapiWindow>();
        services.AddScoped<MainWindowViewModel>();
        services.AddScoped<MainWindow>();

        services.AddScoped<DashboardViewModel>();
        services.AddScoped<DashboardPage>();
        services.AddScoped<BulkSendViewModel>();
        services.AddScoped<BulkSendPage>();
        services.AddScoped<SenderNumbersViewModel>();
        services.AddScoped<SenderNumbersPage>();
        services.AddScoped<TemplatesViewModel>();
        services.AddScoped<TemplatesPage>();
        services.AddScoped<HistoryViewModel>();
        services.AddScoped<HistoryPage>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<SettingsPage>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "Lỗi không xử lý");
        var ui = _host?.Services.GetService<UiService>();
        if (ui is not null)
        {
            ui.Error(e.Exception);
            e.Handled = true;
        }
    }
}
