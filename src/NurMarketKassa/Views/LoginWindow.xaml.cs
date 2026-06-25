using NurMarketKassa.Services;
using NurMarketKassa.ViewModels;
using NurMarketKassa.Views.Dialogs;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private bool _passwordVisible;

        public LoginWindow()
        {
            InitializeComponent();

            _viewModel = new LoginViewModel(ConfirmExit, ShutdownApplication);
            DataContext = _viewModel;

            var prefs = UserPreferences.Instance;
            EmailBox.Text = prefs.LastLoginEmail;
            if (!string.IsNullOrEmpty(prefs.LastLoginPassword))
            {
                PasswordBox.Password = prefs.LastLoginPassword;
                VisiblePasswordBox.Text = prefs.LastLoginPassword;
            }

            EmailBox.TextChanged += (_, _) => UpdateEmailValidIcon();

            Loaded += async (_, _) =>
            {
                if (UserPreferences.Instance.Fullscreen)
                {
                    WindowStyle = WindowStyle.None;
                    ResizeMode = ResizeMode.NoResize;
                    WindowState = WindowState.Maximized;
                }

                UpdateEmailValidIcon();
                await TryOfflineBootstrapAsync().ConfigureAwait(true);
            };
        }

        private void UpdateEmailValidIcon()
        {
            var email = EmailBox.Text.Trim();
            EmailValidIcon.Visibility = email.Contains('@') && email.Contains('.')
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void FluentField_GotFocus(object sender, RoutedEventArgs e)
        {
            AnimateFieldBorder(GetFieldBorder(sender), true);
        }

        private void FluentField_LostFocus(object sender, RoutedEventArgs e)
        {
            AnimateFieldBorder(GetFieldBorder(sender), false);
        }

        private Border GetFieldBorder(object sender)
        {
            if (sender == EmailBox || sender == VisiblePasswordBox)
                return sender == EmailBox ? EmailFieldBorder : PasswordFieldBorder;

            if (sender == PasswordBox)
                return PasswordFieldBorder;

            return null;
        }

        private void AnimateFieldBorder(Border border, bool focused)
        {
            if (border == null) return;

            var targetColor = focused
                ? ((SolidColorBrush)FindResource("LoginFocusBrush")).Color
                : ((SolidColorBrush)FindResource("LoginBorderBrush")).Color;

            var brush = border.BorderBrush as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(brush?.Color ?? Colors.Transparent);
                border.BorderBrush = brush;
            }

            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            SetPasswordVisible(!_passwordVisible);
        }

        private void SetPasswordVisible(bool show)
        {
            _passwordVisible = show;
            TogglePasswordIcon.Text = show ? "\uED1A" : "\uE7B3";

            if (show)
            {
                VisiblePasswordBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                VisiblePasswordBox.Visibility = Visibility.Visible;
                VisiblePasswordBox.Focus();
                VisiblePasswordBox.SelectAll();
            }
            else
            {
                PasswordBox.Password = VisiblePasswordBox.Text;
                VisiblePasswordBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordBox.Focus();
                PasswordBox.SelectAll();
            }
        }

        private void AdminSupport_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            OverlayGrid.Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
            OverlayGrid.IsHitTestVisible = true;

            var support = new AdminSupportWindow { Owner = this };
            support.ShowDialog();

            OverlayGrid.Background = Brushes.Transparent;
            OverlayGrid.IsHitTestVisible = false;
        }

        private void EmailBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return) return;

            if (_passwordVisible)
                VisiblePasswordBox.Focus();
            else
                PasswordBox.Focus();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
                _ = TryLoginAsync();
        }

        private void VisiblePasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
                _ = TryLoginAsync();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

        private async Task TryLoginAsync()
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";

            string email = EmailBox.Text.Trim();
            string password = _passwordVisible
                ? VisiblePasswordBox.Text
                : PasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Введите email и пароль.");
                return;
            }

            LoginButton.IsEnabled = false;
            try
            {
                var sessionRestored = await App.AuthApi
                    .TryRestoreSessionViaRefreshAsync(email, CancellationToken.None)
                    .ConfigureAwait(true);

                if (!sessionRestored)
                    await App.AuthApi.LoginAsync(email, password, CancellationToken.None);

                await CompanyInfoService.RefreshAsync(App.AuthApi, CancellationToken.None).ConfigureAwait(true);
                App.AuditDb.LogEvent("auth", "login", new { email }, App.CurrentUserId);

                OfflineAuthSessionStore.SaveFromApi(App.AuthApi, email);
                var session = OfflineAuthSessionStore.TryLoad();
                if (session != null)
                    App.CurrentUserId = session.UserId;

                AccountCatalogIsolation.PrepareForAuthenticatedUser(email, App.CurrentUserId);

                App.IsOfflineBootstrap = false;
                App.OfflineBootstrapMessage = null;

                var prefs = UserPreferences.Instance;
                prefs.LastLoginEmail = email;
                prefs.LastLoginPassword = password;
                prefs.SaveToDisk();

                await EnterMainWindowAsync().ConfigureAwait(true);
                return;
            }
            catch (ApiException ex)
            {
                ShowError(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                ShowError(string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message);
            }
            catch (TaskCanceledException)
            {
                ShowError("Превышено время ожидания.");
            }
            finally
            {
                if (IsVisible)
                    LoginButton.IsEnabled = true;
            }
        }

        private async Task TryOfflineBootstrapAsync()
        {
            var session = OfflineAuthSessionStore.TryLoad();
            if (!OfflineAuthSessionStore.IsUsable(session))
                return;

            try
            {
                if (await App.AuthApi.CanReachApiAsync(CancellationToken.None).ConfigureAwait(true))
                    return;
            }
            catch
            {
                /* treat as offline */
            }

            App.AuthApi.RestoreOfflineSession(session!);
            CompanyInfoService.RestoreFromOfflineSession();
            App.CurrentUserId = session!.UserId;
            AccountCatalogIsolation.PrepareForAuthenticatedUser(session.Login, session.UserId);
            OfflinePosStateStore.RestoreToApp();
            App.IsOfflineBootstrap = true;
            App.OfflineBootstrapMessage =
                $"Оффлайн режим. Последний вход: {session.CashierName} ({session.LastAuthAt.LocalDateTime:dd.MM.yyyy HH:mm}).";

            PosLogger.Log(App.OfflineBootstrapMessage, "OFFLINE");
            await EnterMainWindowAsync().ConfigureAwait(true);
        }

        private async Task EnterMainWindowAsync()
        {
            LoadingStatusText.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = false;
            ExitButton.IsEnabled = false;

            var progress = new Progress<string>(status => LoadingStatusText.Text = status);

            try
            {
                var mainWindow = App.GetRequiredService<MainWindow>();
                await mainWindow.InitializeApplicationAsync(progress).ConfigureAwait(true);

                Application.Current.MainWindow = mainWindow;
                mainWindow.WindowState = WindowState.Maximized;
                mainWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                LoadingStatusText.Visibility = Visibility.Collapsed;
                ShowError("Не удалось загрузить кассу: " + ex.Message);
                LoginButton.IsEnabled = true;
                ExitButton.IsEnabled = true;
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private MessageBoxResult ConfirmExit() =>
            ExitConfirmationDialog.Show(this) ? MessageBoxResult.Yes : MessageBoxResult.No;

        private static void ShutdownApplication()
        {
            App.ExitWithoutLoginRedirect = true;
            Application.Current.Shutdown();
        }
    }
}
