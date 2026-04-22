using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;

namespace NurMarketKassa;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static NurMarketApiClient Api { get; private set; } = null!;
    public static CartSession Cart { get; } = new();
    public static OfflineSalesSyncService OfflineSync { get; private set; } = null!;
    public static string? PosCashboxId { get; set; }

    /// <summary>Если true, закрытие главного окна не перенаправляет на экран входа (полное завершение приложения).</summary>
    internal static bool ExitWithoutLoginRedirect { get; set; }

    /// <summary>Человекочитаемое имя кассы из списка касс API (не UUID).</summary>
    public static string? PosCashboxDisplayName { get; set; }

    public static string? ActiveShiftId { get; set; }

    private HttpClient? _http;

    static App()
    {
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(TouchKeyboard.OnPreviewKeyboardFocus),
            handledEventsToo: true);
    }

    public static void ApplyTheme(bool dark)
    {
        var uri = new Uri(dark ? "Themes/AppThemeDark.xaml" : "Themes/AppThemeLight.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Current.Resources.MergedDictionaries.Clear();
        Current.Resources.MergedDictionaries.Add(dict);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            PosLogger.Log(
                $"DispatcherUnhandledException: {args.Exception.GetType().FullName}: {args.Exception.Message} | {args.Exception.StackTrace}",
                "ERROR");
            try
            {
                MessageBox.Show(
                    "Ошибка интерфейса:\n\n" + args.Exception.Message + "\n\n" + args.Exception.GetType().FullName,
                    "Nur Market — Касса",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                /* ignore */
            }

            args.Handled = true;
        };

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Settings = AppSettings.Load();
        UserPreferences.LoadFromDiskAndMergeDefaults(Settings);
        ApplyTheme(UserPreferences.Instance.DarkTheme);
        AutostartHelper.SyncFromPreference(UserPreferences.Instance.Autostart);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(55) };
        Api = new NurMarketApiClient(_http, Settings);
        OfflineSync = new OfflineSalesSyncService(Api);

        base.OnStartup(e);
        var login = new Views.LoginWindow();
        login.Show();
        _ = Dispatcher.BeginInvoke(() => OfflineSync.Start(), System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cart.Dispose();
        OfflineSync.Dispose();
        Api.Dispose();
        _http?.Dispose();
        base.OnExit(e);
    }

}
