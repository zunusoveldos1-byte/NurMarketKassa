using NurMarketKassa.Services;
using NurMarketKassa.ViewModels;
using NurMarketKassa.Views.Dialogs;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private readonly AuthService _authService;
        private bool _passwordVisible;
        private bool _suppressPasswordSync;
        private bool _rememberMe;
        private bool _enteringMain;

        public LoginWindow(LoginViewModel viewModel, AuthService authService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _authService = authService;
            DataContext = _viewModel;
            _viewModel.LoginSuccess += OnLoginSuccess;

            var remembered = _authService.TryGetLastRememberedUser();
            if (remembered != null)
            {
                EmailBox.Text = remembered.Email;
                _viewModel.Username = remembered.Email;
                _rememberMe = true;
                RememberMeCheckBox.IsChecked = true;

                var password = WindowsDpapiHelper.UnprotectFromBase64(remembered.PasswordEncrypted);
                PasswordBox.Password = password;
                VisiblePasswordBox.Text = password;
                _viewModel.Password = password;
            }
            else
            {
                EmailBox.Text = UserPreferences.Instance.LastLoginEmail ?? "";
                _viewModel.Username = EmailBox.Text;
            }

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (UserPreferences.Instance.Fullscreen)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }

            UpdateEmailValidIcon();
        }

        private void OnClosed(object sender, EventArgs e) =>
            _viewModel.LoginSuccess -= OnLoginSuccess;

        private async Task OnLoginSuccess()
        {
            if (_enteringMain)
                return;

            _enteringMain = true;
            try
            {
                var email = _viewModel.Username.Trim();

                _viewModel.SetLoadingStatus("Загрузка компании…");
                await CompanyInfoService.RefreshAsync(App.AuthApi, CancellationToken.None).ConfigureAwait(true);
                App.AuditDb.LogEvent("auth", "login", new { email }, App.CurrentUserId);

                var session = _authService.TryLoadOfflineSession();
                AccountCatalogIsolation.PrepareForAuthenticatedUser(email, App.CurrentUserId);
                _authService.SaveRememberedCredentials(
                    email,
                    _viewModel.Password,
                    _rememberMe,
                    App.CurrentUserId,
                    session?.CashierName);

                UserPreferences.Instance.LastLoginEmail = email;
                UserPreferences.Instance.LastLoginPassword = "";
                UserPreferences.Instance.SaveToDisk();

                App.IsOfflineBootstrap = false;
                App.OfflineBootstrapMessage = null;

                _viewModel.SetLoadingStatus("Загрузка кассы…");
                var mainWindow = App.GetRequiredService<MainWindow>();
                var progress = new Progress<string>(status =>
                {
                    if (!string.IsNullOrWhiteSpace(status))
                        _viewModel.SetLoadingStatus(status);
                });

                await mainWindow.InitializeApplicationAsync(progress, CancellationToken.None)
                    .ConfigureAwait(true);

                Application.Current.MainWindow = mainWindow;
                mainWindow.WindowState = WindowState.Maximized;
                mainWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                _enteringMain = false;
                _viewModel.ReportError("Не удалось загрузить кассу: " + ex.Message);
            }
        }

        private void UpdateEmailValidIcon()
        {
            var username = _viewModel.Username;
            EmailValidIcon.Visibility =
                !string.IsNullOrWhiteSpace(username) && username.Contains('@') && username.Contains('.')
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _viewModel.Username = EmailBox.Text;
            UpdateEmailValidIcon();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressPasswordSync || _passwordVisible)
                return;

            _viewModel.Password = PasswordBox.Password;
        }

        private void VisiblePasswordBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressPasswordSync || !_passwordVisible)
                return;

            _viewModel.Password = VisiblePasswordBox.Text;
        }

        private void RememberMeCheckBox_Changed(object sender, RoutedEventArgs e) =>
            _rememberMe = RememberMeCheckBox.IsChecked == true;

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

            _suppressPasswordSync = true;
            try
            {
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

                _viewModel.Password = show ? VisiblePasswordBox.Text : PasswordBox.Password;
            }
            finally
            {
                _suppressPasswordSync = false;
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
            if (e.Key == Key.Return && _viewModel.LoginCommand.CanExecute(null))
                _viewModel.LoginCommand.Execute(null);
        }

        private void VisiblePasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && _viewModel.LoginCommand.CanExecute(null))
                _viewModel.LoginCommand.Execute(null);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = ConfirmExit();
            if (result is not MessageBoxResult.Yes)
                return;

            ShutdownApplication();
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
