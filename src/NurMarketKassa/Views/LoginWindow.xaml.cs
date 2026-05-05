using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NurMarketKassa.Services;
using NurMarketKassa.ViewModels;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

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

            Loaded += (_, _) =>
            {
                if (UserPreferences.Instance.Fullscreen)
                {
                    WindowStyle = WindowStyle.None;
                    ResizeMode = ResizeMode.NoResize;
                    WindowState = WindowState.Maximized;
                }
            };
        }

        private void EmailBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return) return;

            if (ShowPasswordCheck.IsChecked.GetValueOrDefault())
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

        private void ShowPasswordCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowPasswordCheck.IsChecked.GetValueOrDefault())
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

        private async void LoginButton_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

        private async Task TryLoginAsync()
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";

            string email = EmailBox.Text.Trim();
            string password = ShowPasswordCheck.IsChecked.GetValueOrDefault()
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
                await App.Api.LoginAsync(email, password, CancellationToken.None);

                var prefs = UserPreferences.Instance;
                prefs.LastLoginEmail = email;
                prefs.LastLoginPassword = password;
                prefs.SaveToDisk();

                var mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                Close();
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
                LoginButton.IsEnabled = true;
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private MessageBoxResult ConfirmExit()
        {
            return MessageBox.Show(this,
                "Вы действительно хотите выйти из программы?",
                "Подтверждение выхода",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
        }

        private static void ShutdownApplication()
        {
            App.ExitWithoutLoginRedirect = true;
            Application.Current.Shutdown();
        }
    }
}