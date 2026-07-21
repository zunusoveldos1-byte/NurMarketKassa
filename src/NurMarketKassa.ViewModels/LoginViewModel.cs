using System.Collections.ObjectModel;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.ViewModels;

/// <summary>Portable login screen view-model (WPF / Avalonia).</summary>
public sealed class LoginViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ConnectivityCheckInterval = TimeSpan.FromSeconds(5);

    private readonly IAppSession _session;
    private readonly IAuthService _authService;
    private readonly ILocalAccountsStore _localAccounts;
    private readonly IConnectivityService _connectivity;
    private readonly IOfflineLoginSupport _offlineLogin;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private string _username = "";
    private string _password = "";
    private bool _isLoading;
    private string _loadingStatus = "";
    private string _errorMessage = "";
    private bool _hasError;
    private bool _rememberMe;
    private bool _isOfflineMode;

    public LoginViewModel(
        IAppSession session,
        IAuthService authService,
        ILocalAccountsStore localAccounts,
        IConnectivityService connectivity,
        IOfflineLoginSupport offlineLogin)
    {
        _session = session;
        _authService = authService;
        _localAccounts = localAccounts;
        _connectivity = connectivity;
        _offlineLogin = offlineLogin;

        SavedAccounts = new ObservableCollection<string>();
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsLoading);

        _localAccounts.EnsureSchema();
        ReloadSavedAccounts();
        _ = MonitorConnectivityAsync(_lifetimeCts.Token);
    }

    public ObservableCollection<string> SavedAccounts { get; }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value ?? "");
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? "");
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    public bool IsOfflineMode
    {
        get => _isOfflineMode;
        private set
        {
            if (!SetProperty(ref _isOfflineMode, value))
                return;
            OnPropertyChanged(nameof(LoginButtonText));
            OnPropertyChanged(nameof(UseSavedAccountsPicker));
            OnPropertyChanged(nameof(LoginButtonIsOfflineAccent));
        }
    }

    public bool UseSavedAccountsPicker => IsOfflineMode || SavedAccounts.Count > 0;

    public bool LoginButtonIsOfflineAccent => IsOfflineMode;

    public string LoginButtonText => IsOfflineMode ? "Войти в офлайн-режим" : "Войти";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;
            RaiseLoginCanExecuteChanged();
        }
    }

    /// <summary>Текст статуса поверх экрана входа во время авторизации и загрузки кассы.</summary>
    public string LoadingStatus
    {
        get => _loadingStatus;
        private set => SetProperty(ref _loadingStatus, value ?? "");
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value ?? ""))
                return;
            HasError = !string.IsNullOrWhiteSpace(_errorMessage);
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public ICommand LoginCommand { get; }

    /// <summary>
    /// После записи сессии в <see cref="IAppSession"/> — асинхронная загрузка главного окна.
    /// Пока обработчики не завершатся, <see cref="IsLoading"/> остаётся true.
    /// </summary>
    public event Func<Task>? LoginSuccess;

    public void ReportError(string message)
    {
        ErrorMessage = message;
        LoadingStatus = "";
        IsLoading = false;
        RaiseLoginCanExecuteChanged();
    }

    public void SetLoadingStatus(string status) => LoadingStatus = status;

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
    }

    private async Task LoginAsync()
    {
        ErrorMessage = "";

        var username = Username.Trim();
        var password = Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Введите логин и пароль.";
            return;
        }

        IsLoading = true;
        LoadingStatus = IsOfflineMode ? "Офлайн-вход…" : "Авторизация…";
        try
        {
            if (IsOfflineMode)
            {
                if (!await TryLoginOfflineAsync(username, password).ConfigureAwait(true))
                    return;

                LoadingStatus = "Загрузка кассы…";
                await RaiseLoginSuccessAsync().ConfigureAwait(true);
                return;
            }

            var result = await _authService
                .LoginAsync(username, password, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Не удалось выполнить вход."
                    : result.ErrorMessage;
                return;
            }

            ApplySession(result);

            if (RememberMe)
            {
                var displayName = result.PosCashboxDisplayName ?? username;
                _localAccounts.Upsert(username, password, displayName);
                ReloadSavedAccounts();
            }

            LoadingStatus = "Загрузка кассы…";
            await RaiseLoginSuccessAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Превышено время ожидания.";
        }
        catch (Exception ex)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Ошибка авторизации."
                : ex.Message;
        }
        finally
        {
            if (IsLoading)
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }
    }

    private async Task RaiseLoginSuccessAsync()
    {
        var handlers = LoginSuccess;
        if (handlers == null)
            return;

        foreach (var d in handlers.GetInvocationList())
        {
            if (d is Func<Task> handler)
                await handler().ConfigureAwait(true);
        }
    }

    private Task<bool> TryLoginOfflineAsync(string username, string password)
    {
        if (!_localAccounts.ValidatePassword(username, password))
        {
            ErrorMessage =
                "Неверные данные. Для офлайн-входа включите «Запомнить меня» при предыдущем онлайн-входе.";
            return Task.FromResult(false);
        }

        var bootstrap = _offlineLogin.TryBootstrapOfflineSession(username);
        if (bootstrap == null || string.IsNullOrWhiteSpace(bootstrap.UserId))
        {
            ErrorMessage = "Нет сохранённой сессии. Сначала выполните вход при наличии интернета.";
            return Task.FromResult(false);
        }

        var account = _localAccounts.FindByEmail(username);
        _session.CurrentUserId = bootstrap.UserId;
        _session.IsOfflineBootstrap = true;
        _session.OfflineBootstrapMessage = bootstrap.OfflineMessage
            ?? $"Оффлайн режим. Последний вход: {account?.DisplayName ?? bootstrap.DisplayName ?? username}.";

        return Task.FromResult(true);
    }

    private void ApplySession(AuthResult result)
    {
        _session.CurrentUserId = result.UserId;
        _session.ActiveShiftId = result.ActiveShiftId;
        _session.ActiveTerminal = result.ActiveTerminal;
        _session.PosCashboxDisplayName = result.PosCashboxDisplayName;
        _session.IsOfflineBootstrap = result.IsOfflineBootstrap;
        _session.OfflineBootstrapMessage = result.OfflineBootstrapMessage;
    }

    private void ReloadSavedAccounts()
    {
        SavedAccounts.Clear();
        foreach (var email in _localAccounts.GetSavedEmails())
            SavedAccounts.Add(email);

        OnPropertyChanged(nameof(UseSavedAccountsPicker));
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

    private async Task UpdateConnectivityStateAsync()
    {
        try
        {
            var online = await _connectivity.IsOnlineAsync(_lifetimeCts.Token).ConfigureAwait(false);
            IsOfflineMode = !online;
        }
        catch
        {
            IsOfflineMode = true;
        }
    }

    private void RaiseLoginCanExecuteChanged()
    {
        if (LoginCommand is AsyncRelayCommand asyncCommand)
            asyncCommand.RaiseCanExecuteChanged();
    }
}
