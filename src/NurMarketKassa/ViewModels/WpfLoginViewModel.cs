using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NurMarketKassa.Services;
using NurMarketKassa.Views;

namespace NurMarketKassa.ViewModels;

public sealed class WpfLoginViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan ConnectivityCheckInterval = TimeSpan.FromSeconds(5);

    private readonly AuthService _authService;
    private readonly SyncService _syncService;
    private readonly Func<MessageBoxResult> _confirmExit;
    private readonly Action _exitApplication;
    private readonly Func<MainWindow> _mainWindowFactory;
    private readonly CancellationTokenSource _connectivityCts = new();

    private string _email = "";
    private string _password = "";
    private bool _rememberMe;
    private bool _isOfflineMode;
    private bool _isBusy;
    private string _errorMessage = "";
    private string _loadingStatus = "";
    private bool _hasError;
    private bool _isLoadingVisible;

    public WpfLoginViewModel(
        AuthService authService,
        SyncService syncService,
        Func<MessageBoxResult> confirmExit,
        Action exitApplication,
        Func<MainWindow> mainWindowFactory)
    {
        _authService = authService;
        _syncService = syncService;
        _confirmExit = confirmExit;
        _exitApplication = exitApplication;
        _mainWindowFactory = mainWindowFactory;

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
        ExitApplicationCommand = new RelayCommand(ExitApplication);

        var remembered = _authService.TryGetLastRememberedUser();
        if (remembered != null)
        {
            Email = remembered.Email;
            RememberMe = true;
        }
        else
        {
            Email = UserPreferences.Instance.LastLoginEmail;
        }
    }

    public string Email
    {
        get => _email;
        set { _email = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmailValid)); }
    }

    public string Password
    {
        get => _password;
        set { _password = value ?? ""; OnPropertyChanged(); }
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set { _rememberMe = value; OnPropertyChanged(); }
    }

    public bool IsOfflineMode
    {
        get => _isOfflineMode;
        private set
        {
            if (_isOfflineMode == value)
                return;
            _isOfflineMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoginButtonText));
        }
    }

    public string LoginButtonText => IsOfflineMode ? "Войти в офлайн-режиме" : "Войти";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value ?? "";
            HasError = !string.IsNullOrWhiteSpace(_errorMessage);
            OnPropertyChanged();
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set { _hasError = value; OnPropertyChanged(); }
    }

    public string LoadingStatus
    {
        get => _loadingStatus;
        private set
        {
            _loadingStatus = value ?? "";
            IsLoadingVisible = !string.IsNullOrWhiteSpace(_loadingStatus);
            OnPropertyChanged();
        }
    }

    public bool IsLoadingVisible
    {
        get => _isLoadingVisible;
        private set { _isLoadingVisible = value; OnPropertyChanged(); }
    }

    public bool IsEmailValid =>
        !string.IsNullOrWhiteSpace(Email) && Email.Contains('@') && Email.Contains('.');

    public ICommand LoginCommand { get; }
    public ICommand ExitApplicationCommand { get; }

    public async Task InitializeAsync()
    {
        _ = MonitorConnectivityAsync(_connectivityCts.Token);
        await UpdateConnectivityStateAsync().ConfigureAwait(true);
        await TryOfflineBootstrapAsync().ConfigureAwait(true);
    }

    private async Task UpdateConnectivityStateAsync()
    {
        try
        {
            var online = await _authService.CheckInternetAsync(_connectivityCts.Token).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() => IsOfflineMode = !online);
        }
        catch
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsOfflineMode = true);
        }
    }

    public void Dispose()
    {
        _connectivityCts.Cancel();
        _connectivityCts.Dispose();
    }

    private async Task MonitorConnectivityAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ConnectivityCheckInterval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await UpdateConnectivityStateAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsOfflineMode = true);
            }

            try
            {
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task LoginAsync()
    {
        ErrorMessage = "";

        var email = Email.Trim();
        var password = Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Введите email и пароль.";
            return;
        }

        IsBusy = true;
        try
        {
            if (IsOfflineMode)
            {
                await LoginOfflineAsync(email, password).ConfigureAwait(true);
                return;
            }

            await _authService.LoginOnlineAsync(email, password, CancellationToken.None).ConfigureAwait(true);
            await CompanyInfoService.RefreshAsync(App.AuthApi, CancellationToken.None).ConfigureAwait(true);
            App.AuditDb.LogEvent("auth", "login", new { email }, App.CurrentUserId);

            await _authService.PersistOfflineSessionAsync(email, CancellationToken.None).ConfigureAwait(true);
            var session = _authService.TryLoadOfflineSession();
            if (session != null)
                App.CurrentUserId = session.UserId;

            if (string.IsNullOrWhiteSpace(App.CurrentUserId))
            {
                ErrorMessage = "Не удалось получить идентификатор пользователя.";
                return;
            }

            AccountCatalogIsolation.PrepareForAuthenticatedUser(email, App.CurrentUserId);
            _authService.SaveRememberedCredentials(
                email,
                password,
                RememberMe,
                App.CurrentUserId,
                session?.CashierName);

            UserPreferences.Instance.LastLoginEmail = email;
            UserPreferences.Instance.LastLoginPassword = "";
            UserPreferences.Instance.SaveToDisk();

            App.IsOfflineBootstrap = false;
            App.OfflineBootstrapMessage = null;

            await EnterMainWindowAsync().ConfigureAwait(true);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "Превышено время ожидания.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginOfflineAsync(string email, string password)
    {
        LoadingStatus = "Проверка подключения к серверу…";
        try
        {
            await _syncService.ProbeNowAsync().ConfigureAwait(true);

            if (_syncService.IsOnline)
            {
                IsOfflineMode = false;
                LoadingStatus = "Обнаружено стабильное подключение. Пожалуйста, войдите в систему в онлайн-режиме.";
                MessageBox.Show(
                    "Сервер снова доступен. Пожалуйста, используйте стандартный вход по логину и паролю.",
                    "Связь восстановлена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }
        catch (TaskCanceledException)
        {
            /* таймаут — продолжаем офлайн-вход */
        }
        catch (HttpRequestException)
        {
            /* нет сети — продолжаем офлайн-вход */
        }
        finally
        {
            if (!_syncService.IsOnline)
                LoadingStatus = "";
        }

        if (!_authService.ValidateOfflineCredentials(email, password))
        {
            ErrorMessage = "Неверные данные. Для офлайн-входа включите «Запомнить меня» при предыдущем онлайн-входе.";
            return;
        }

        var session = _authService.TryLoadOfflineSession();
        if (!_authService.IsOfflineSessionUsable(session))
        {
            ErrorMessage = "Нет сохранённой сессии. Сначала выполните вход при наличии интернета.";
            return;
        }

        _authService.RestoreOfflineSession(session!);
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

    private async Task TryOfflineBootstrapAsync()
    {
        if (App.SkipOfflineAutoLogin)
        {
            App.SkipOfflineAutoLogin = false;
            return;
        }

        var session = _authService.TryLoadOfflineSession();
        if (!_authService.IsOfflineSessionUsable(session))
            return;

        try
        {
            if (await _authService.CheckInternetAsync(CancellationToken.None).ConfigureAwait(true))
                return;
        }
        catch
        {
            /* treat as offline */
        }

        _authService.RestoreOfflineSession(session!);
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
        LoadingStatus = "Загрузка кассы…";
        IsBusy = true;

        var progress = new Progress<string>(status => LoadingStatus = status);

        try
        {
            var mainWindow = _mainWindowFactory();
            await mainWindow.InitializeApplicationAsync(progress).ConfigureAwait(true);

            Application.Current.MainWindow = mainWindow;
            mainWindow.WindowState = WindowState.Maximized;
            mainWindow.Show();

            foreach (Window window in Application.Current.Windows)
            {
                if (window is LoginWindow loginWindow)
                {
                    loginWindow.Close();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LoadingStatus = "";
            ErrorMessage = "Не удалось загрузить кассу: " + ex.Message;
            IsBusy = false;
        }
    }

    private void ExitApplication()
    {
        var result = _confirmExit();
        if (result is not MessageBoxResult.Yes)
            return;

        _exitApplication();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
