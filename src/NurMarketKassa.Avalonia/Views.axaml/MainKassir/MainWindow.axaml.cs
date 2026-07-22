using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.AvaloniaHost.Views;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels.Main;

namespace NurMarketKassa.AvaloniaHost.Views.MainKassir;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IAppSession _session;
    private readonly MainWindowHostBridge _hostBridge;
    private readonly ICashShiftService _cashShiftService;
    private readonly IShiftStateService _shiftStateService;
    private readonly IUserPrompts _prompts;
    private readonly CancellationTokenSource _windowCts = new();

    private bool _appInitialized;
    private bool _allowMainWindowClose;
    private bool _logoutNavigateScheduled;
    private decimal? _shiftCashBalance;

    /// <summary>Parameterless ctor required by Avalonia XAML runtime loader / designer.</summary>
    public MainWindow() : this(
        ResolveService<MainWindowViewModel>(),
        ResolveService<IAppSession>(),
        ResolveService<MainWindowHostBridge>(),
        ResolveService<ICashShiftService>(),
        ResolveService<IShiftStateService>(),
        ResolveService<IUserPrompts>())
    {
    }

    private static T ResolveService<T>() where T : notnull
    {
        var sp = App.AppHost?.Services
            ?? throw new InvalidOperationException($"{typeof(T).Name} requires running AppHost DI.");
        return sp.GetRequiredService<T>();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        IAppSession session,
        MainWindowHostBridge hostBridge,
        ICashShiftService cashShiftService,
        IShiftStateService shiftStateService,
        IUserPrompts prompts)
    {
        _viewModel = viewModel;
        _session = session;
        _hostBridge = hostBridge;
        _cashShiftService = cashShiftService;
        _shiftStateService = shiftStateService;
        _prompts = prompts;
        _hostBridge.Window = this;
        WireDialogBridge();

        InitializeComponent();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;

        ApplyFullscreenPreference();
        _viewModel.Toolbar.UpdateThemeGlyph(UserPreferences.Instance.DarkTheme);
    }

    public async Task InitializeApplicationAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_appInitialized)
            return;

        progress?.Report("Загрузка кассы...");

        if (AccountCatalogIsolation.RequireForcedCatalogSync)
            _viewModel.Catalog.StatusText = "Требуется синхронизация каталога для нового пользователя.";

        progress?.Report("Загрузка профиля...");
        try
        {
            await CompanyInfoService.RefreshAsync(App.AuthApi, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            /* offline bootstrap */
        }

        progress?.Report("Обновление смены...");
        await RefreshShiftStateAsync(cancellationToken).ConfigureAwait(true);

        progress?.Report("Подготовка рабочего места...");
        await _viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(_session.OfflineBootstrapMessage))
            _viewModel.Catalog.StatusText = _session.OfflineBootstrapMessage!;

        if (AccountCatalogIsolation.RequireForcedCatalogSync)
            AccountCatalogIsolation.ClearForcedCatalogSyncFlag();

        _appInitialized = true;
    }

    // ----------------------------------------------------------------
    //  Обработчики событий шапки и панели чека (MainWindow.axaml)
    // ----------------------------------------------------------------

    /// <summary>Переключение темы Light/Dark (иконка луны/солнца в шапке).</summary>
    private void ToggleTheme_Click(object? sender, RoutedEventArgs e) => ToggleTheme();

    /// <summary>Создание нового чека (кнопка «+» рядом с вкладками чеков).</summary>
    private void NewReceipt_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.Basket.CreateNewReceipt();

    /// <summary>Открытие смены (кнопка «🔓 Открыть смену» в шапке).</summary>
    private async void OpenShift_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await OpenShiftAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            /* cancelled */
        }
    }

    /// <summary>Закрытие смены (кнопка «🔒 Закрыть смену» в шапке).</summary>
    private async void CloseShift_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await CloseShiftAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            /* cancelled */
        }
    }

    internal void ToggleTheme()
    {
        var app = Application.Current;
        if (app is null)
            return;

        var isDarkNow = app.ActualThemeVariant == ThemeVariant.Dark;
        var newIsDark = !isDarkNow;

        app.RequestedThemeVariant = newIsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var prefs = UserPreferences.Instance;
        prefs.DarkTheme = newIsDark;
        prefs.SaveToDisk();
        _viewModel.Toolbar.UpdateThemeGlyph(newIsDark);
    }

    internal void ToggleKeyboard()
    {
        try
        {
            App.GetRequiredService<IOperatingSystemKeyboardService>().ShowSystemKeyboard();
        }
        catch (Exception ex)
        {
            PosLogger.Log($"OSK failed, fallback to FrmKeyboard: {ex.Message}", "UI");
            if (FrmKeyboard.CurrentForm is FrmKeyboard existing && existing.IsVisible)
                FrmKeyboard.KillKeyboard();
            else
                FrmKeyboard.ShowKeyboard(this);
        }
    }

    internal Task OpenShiftAsync()
    {
        var dlg = App.GetRequiredService<OpenShiftDialog>();
        dlg.SuggestedBalance = _shiftCashBalance;
        if (PosDialogHost.Show(dlg, this) != true)
            return Task.CompletedTask;

        return ApplyShiftOpenedAsync(dlg.OpeningCash);
    }

    internal async Task CloseShiftAsync()
    {
        if (!_session.IsShiftOpen)
            return;

        var dlg = App.GetRequiredService<CloseShiftDialog>();
        dlg.SuggestedBalance = _shiftCashBalance;
        if (PosDialogHost.Show(dlg, this) != true)
            return;

        await ApplyShiftClosedAsync(dlg.ClosingCash).ConfigureAwait(true);
    }

    internal void NavigateWarehouse() => ShowModuleWindow<WarehouseWindow>();

    internal void NavigateShifts() => ShowModuleWindow<ShiftsHistoryWindow>();

    internal void NavigateCashOperations() => _ = OpenCashOperationsAsync();

    internal void NavigateReturn()
    {
        _viewModel.CloseSideMenu();
        var dlg = App.GetRequiredService<ReturnSaleDialog>();
        PosDialogHost.Show(dlg, this);
    }

    internal void NavigateFinance() => ShowModuleWindow<FinanceWindow>();

    internal void NavigateSales() => ShowModuleWindow<SalesWindow>();

    internal void NavigateSettings() => ShowModuleWindow<PosSettingsWindow>();

    internal async void LogoutAsync()
    {
        try
        {
            if (_session.IsShiftOpen)
            {
                var shiftResult = ShiftNotClosedDialog.Prompt(this);
                if (shiftResult == Views.Dialogs.ShiftNotClosedDialogResult.Cancel)
                    return;

                if (shiftResult == Views.Dialogs.ShiftNotClosedDialogResult.CloseShift)
                    await CloseShiftAsync().ConfigureAwait(true);
            }

            NavigateToLogin();
        }
        catch (OperationCanceledException)
        {
            /* cancelled */
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_appInitialized)
            return;

        await InitializeApplicationAsync(null, _windowCts.Token).ConfigureAwait(true);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!App.ExitWithoutLoginRedirect && !_allowMainWindowClose)
        {
            if (_session.IsShiftOpen)
            {
                e.Cancel = true;
                var shiftResult = ShiftNotClosedDialog.Prompt(this);
                if (shiftResult == Views.Dialogs.ShiftNotClosedDialogResult.Cancel)
                    return;

                if (shiftResult == Views.Dialogs.ShiftNotClosedDialogResult.CloseShift)
                {
                    try
                    {
                        CloseShiftAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        /* ignore */
                    }
                    catch
                    {
                        /* server errors should not block exit */
                    }
                }
            }

            e.Cancel = true;
            if (_logoutNavigateScheduled)
                return;

            _logoutNavigateScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    NavigateToLogin();
                }
                finally
                {
                    _logoutNavigateScheduled = false;
                }
            });
            return;
        }

        try
        {
            _windowCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            /* already disposed */
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _windowCts.Dispose();
        if (ReferenceEquals(_hostBridge.Window, this))
            _hostBridge.Window = null;
    }

    private void OnSideMenuBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        _viewModel.CloseSideMenu();

    private void ShowModuleWindow<T>() where T : Window
    {
        _viewModel.CloseSideMenu();
        var window = App.GetRequiredService<T>();
        window.Show(this);
    }

    private async Task RefreshShiftStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(App.PosCashboxId))
            {
                var rawList = await App.ShiftApi.ConstructionCashboxesListAsync(cancellationToken).ConfigureAwait(true);
                if (CartDisplayHelper.TryFirstCashbox(rawList, out var id, out var displayName))
                {
                    App.PosCashboxId = id;
                    NurMarketKassa.App.PosCashboxId = id;
                    _session.ActiveTerminal = id;
                    _session.PosCashboxDisplayName = displayName;
                    NurMarketKassa.App.PosCashboxDisplayName = displayName;
                }
            }

            await _shiftStateService.RefreshAsync(cancellationToken).ConfigureAwait(true);
            NurMarketKassa.App.SyncToSession(_session);

            if (_session.IsShiftOpen)
                _shiftCashBalance = ShiftBalanceHelper.FindOpenShiftBalance(
                    await App.ShiftApi.ConstructionShiftsListAsync(cancellationToken).ConfigureAwait(true),
                    App.PosCashboxId) ?? OfflinePosStateStore.ReadShiftCashBalance();

            _viewModel.Toolbar.RefreshUserTitle();
            _viewModel.Toolbar.Status.RefreshFromSession();
            UpdateShiftBalanceUi();
            _viewModel.Toolbar.NotifyShiftStateChanged();

            if (_session.IsShiftOpen)
                _viewModel.Catalog.StatusText = "Смена открыта";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"SHIFT refresh failed: {ex.Message}", "SHIFT");
            UpdateShiftBalanceUi();
        }
    }

    private async Task ApplyShiftOpenedAsync(decimal openingCash)
    {
        NurMarketKassa.App.SyncFromSession(_session);
        var result = await _cashShiftService.OpenShiftAsync(openingCash, _windowCts.Token).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _prompts.ShowError(result.ErrorMessage ?? "Не удалось открыть смену.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.InfoMessage))
            _prompts.ShowToast(result.InfoMessage);

        NurMarketKassa.App.SyncToSession(_session);
        App.PosCashboxId = NurMarketKassa.App.PosCashboxId;

        _shiftCashBalance = result.Balance ?? openingCash;
        UpdateShiftBalanceUi();
        _viewModel.Toolbar.NotifyShiftStateChanged();
        _viewModel.SideMenu.ShiftBalanceText = ShiftBalanceHelper.FormatBalance(_shiftCashBalance);
        _viewModel.Catalog.StatusText = $"Смена открыта. Остаток: {openingCash:0.00} сом";
    }

    private async Task ApplyShiftClosedAsync(decimal? closingCash)
    {
        if (!_session.IsShiftOpen)
            return;

        NurMarketKassa.App.SyncFromSession(_session);
        var result = await _cashShiftService.CloseShiftAsync(closingCash, _windowCts.Token).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _prompts.ShowError(result.ErrorMessage ?? "Не удалось закрыть смену.");
            return;
        }

        _viewModel.Basket.ClearAfterShiftClose();
        NurMarketKassa.App.SyncToSession(_session);

        _shiftCashBalance = result.Balance ?? closingCash ?? 0m;
        UpdateShiftBalanceUi();
        _viewModel.Toolbar.NotifyShiftStateChanged();
        _viewModel.SideMenu.ShiftBalanceText = "Смена не открыта";
        _viewModel.Catalog.StatusText = "Смена закрыта.";
    }

    private void UpdateShiftBalanceUi()
    {
        var balanceText = _session.IsShiftOpen
            ? $"Касса: {ShiftBalanceHelper.FormatBalance(_shiftCashBalance)}"
            : "Касса: 0.00 сом";

        _viewModel.Toolbar.Status.SetShiftBalance(_shiftCashBalance ?? 0m);
        _viewModel.SideMenu.ShiftBalanceText = _session.IsShiftOpen
            ? ShiftBalanceHelper.FormatBalance(_shiftCashBalance)
            : "Смена не открыта";

        if (_viewModel.Toolbar.Status.ShiftBalanceText != balanceText)
            _viewModel.Toolbar.Status.ShiftBalanceText = balanceText;
    }

    private void NavigateToLogin()
    {
        FrmKeyboard.KillKeyboard();

        try
        {
            if (!_windowCts.IsCancellationRequested)
                _windowCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            /* already closing */
        }

        App.AuthApi.ClearSession();
        App.PosCashboxId = null;
        _session.ActiveShiftId = null;
        _session.ActiveTerminal = null;
        _session.PosCashboxDisplayName = null;
        _session.IsOfflineBootstrap = false;
        _session.OfflineBootstrapMessage = null;
        App.IsOfflineBootstrap = false;
        App.OfflineBootstrapMessage = null;

        Hide();

        var login = App.GetRequiredService<LoginWindow>();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = login;

        login.Show();

        _allowMainWindowClose = true;
        try
        {
            Close();
        }
        finally
        {
            _allowMainWindowClose = false;
        }
    }

    private void ApplyFullscreenPreference()
    {
        if (!UserPreferences.Instance.Fullscreen)
            return;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        WindowState = WindowState.Maximized;
    }
}
