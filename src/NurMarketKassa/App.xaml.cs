using NurMarketKassa.Configuration;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace NurMarketKassa
{
    public partial class App : Application
    {
        private static ScalePrinterAgentService? _agentService;
        public static AppSettings Settings { get; private set; } = null!;
        public static NurMarketApiClient Api { get; private set; } = null!;
        public static CartSession Cart { get; } = new();
        public static OfflineSalesSyncService OfflineSync { get; private set; } = null!;
        public static string? CurrentUserId { get; set; }
        public static string? PosCashboxId { get; set; }
        internal static bool ExitWithoutLoginRedirect { get; set; }
        public static string? PosCashboxDisplayName { get; set; }
        public static string? ActiveShiftId { get; set; }

        private HttpClient? _http;

        static App()
        {
            // Регистрируем обработчик фокуса для Touch Keyboard
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
            // Игнорируем Velopack (заменено заглушкой)
            try
            {
                // Если нужна реальная поддержка автообновлений – установите Velopack и раскомментируйте:
                // VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления: {ex.Message}");
            }

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

            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, args) =>
            {
                if (args.IsAvailable)
                {
                    Dispatcher.InvokeAsync(() => OfflineSync.TriggerSyncNowAsync());
                }
            };

            base.OnStartup(e);
            var login = new Views.LoginWindow();
            login.Show();
            _ = Dispatcher.BeginInvoke(() => OfflineSync.Start(), System.Windows.Threading.DispatcherPriority.Background);
            _agentService = new ScalePrinterAgentService();
            _agentService.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Cart.Dispose();
            OfflineSync.Dispose();
            Api.Dispose();
            _http?.Dispose();
            _agentService?.Dispose();
            base.OnExit(e);
        }
    }
}