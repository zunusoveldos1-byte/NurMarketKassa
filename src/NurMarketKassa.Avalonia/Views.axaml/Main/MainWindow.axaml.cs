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
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.Services;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels.Main;

namespace NurMarketKassa.AvaloniaHost.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IAppSession _session;
    private readonly MainWindowHostBridge _hostBridge;
    private readonly CancellationTokenSource _windowCts = new();

    private bool _appInitialized;
    private bool _allowMainWindowClose;
    private bool _logoutNavigateScheduled;
    private decimal? _shiftCashBalance;

    /// <summary>Parameterless ctor required by Avalonia XAML runtime loader / designer.</summary>
    public MainWindow() : this(
        ResolveService<MainWindowViewModel>(),
        ResolveService<IAppSession>(),
        ResolveService<MainWindowHostBridge>())
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
        MainWindowHostBridge hostBridge)
    {
        _viewModel = viewModel;
        _session = session;
        _hostBridge = hostBridge;
        _hostBridge.Window = this;

        InitializeComponent();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;

        ApplyFullscreenPreference();
    }

    public async Task InitializeApplicationAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_appInitialized)
            return;

        progress?.Report("Р вЂ”Р В°Р С–РЎР‚РЎС“Р В·Р С”Р В° Р С”Р В°РЎРѓРЎРѓРЎвЂ№...");

        if (AccountCatalogIsolation.RequireForcedCatalogSync)
            _viewModel.Catalog.StatusText = "Р СћРЎР‚Р ВµР В±РЎС“Р ВµРЎвЂљРЎРѓРЎРЏ РЎРѓР С‘Р Р…РЎвЂ¦РЎР‚Р С•Р Р…Р С‘Р В·Р В°РЎвЂ Р С‘РЎРЏ Р С”Р В°РЎвЂљР В°Р В»Р С•Р С–Р В° Р Т‘Р В»РЎРЏ Р Р…Р С•Р Р†Р С•Р С–Р С• Р С—Р С•Р В»РЎРЉР В·Р С•Р Р†Р В°РЎвЂљР ВµР В»РЎРЏ.";

        progress?.Report("Р вЂ”Р В°Р С–РЎР‚РЎС“Р В·Р С”Р В° Р С—РЎР‚Р С•РЎвЂћР С‘Р В»РЎРЏ...");
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

        progress?.Report("Р С›Р В±Р Р…Р С•Р Р†Р В»Р ВµР Р…Р С‘Р Вµ РЎРѓР СР ВµР Р…РЎвЂ№...");
        await RefreshShiftStateAsync(cancellationToken).ConfigureAwait(true);

        progress?.Report("Р СџР С•Р Т‘Р С–Р С•РЎвЂљР С•Р Р†Р С”Р В° РЎР‚Р В°Р В±Р С•РЎвЂЎР ВµР С–Р С• Р СР ВµРЎРѓРЎвЂљР В°...");
        await _viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(_session.OfflineBootstrapMessage))
            _viewModel.Catalog.StatusText = _session.OfflineBootstrapMessage!;

        if (AccountCatalogIsolation.RequireForcedCatalogSync)
            AccountCatalogIsolation.ClearForcedCatalogSyncFlag();

        _appInitialized = true;
    }

    internal void ToggleTheme()
    {
        var prefs = UserPreferences.Instance;
        prefs.DarkTheme = !prefs.DarkTheme;
        prefs.SaveToDisk();
        App.ApplyTheme(prefs.DarkTheme);
    }

    internal void ToggleKeyboard()
    {
        if (FrmKeyboard.CurrentForm is FrmKeyboard existing && existing.IsVisible)
            FrmKeyboard.KillKeyboard();
        else
            FrmKeyboard.ShowKeyboard(this);
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
                    _session.ActiveTerminal = id;
                    _session.PosCashboxDisplayName = displayName;
                }
            }

            _viewModel.Toolbar.RefreshUserTitle();
            _viewModel.Toolbar.Status.RefreshFromSession();
            UpdateShiftBalanceUi();
            _viewModel.Toolbar.NotifyShiftStateChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            UpdateShiftBalanceUi();
        }
    }

    private async Task ApplyShiftOpenedAsync(decimal openingCash)
    {
        _shiftCashBalance = openingCash;
        _session.ActiveShiftId = Guid.NewGuid().ToString("N");
        UpdateShiftBalanceUi();
        _viewModel.Toolbar.NotifyShiftStateChanged();
        _viewModel.SideMenu.ShiftBalanceText = ShiftBalanceHelper.FormatBalance(_shiftCashBalance);
        _viewModel.Catalog.StatusText = $"Р РЋР СР ВµР Р…Р В° Р С•РЎвЂљР С”РЎР‚РЎвЂ№РЎвЂљР В°. Р С›РЎРѓРЎвЂљР В°РЎвЂљР С•Р С”: {openingCash:0.00} РЎРѓР С•Р С";
        await Task.CompletedTask;
    }

    private async Task ApplyShiftClosedAsync(decimal? closingCash)
    {
        _shiftCashBalance = closingCash ?? 0m;
        _session.ActiveShiftId = null;
        UpdateShiftBalanceUi();
        _viewModel.Toolbar.NotifyShiftStateChanged();
        _viewModel.SideMenu.ShiftBalanceText = "Р РЋР СР ВµР Р…Р В° Р Р…Р Вµ Р С•РЎвЂљР С”РЎР‚РЎвЂ№РЎвЂљР В°";
        _viewModel.Catalog.StatusText = "Р РЋР СР ВµР Р…Р В° Р В·Р В°Р С”РЎР‚РЎвЂ№РЎвЂљР В°.";
        await Task.CompletedTask;
    }

    private void UpdateShiftBalanceUi()
    {
        var balanceText = _session.IsShiftOpen
            ? $"Р С™Р В°РЎРѓРЎРѓР В°: {ShiftBalanceHelper.FormatBalance(_shiftCashBalance)}"
            : "Р С™Р В°РЎРѓРЎРѓР В°: 0.00 РЎРѓР С•Р С";

        _viewModel.Toolbar.Status.SetShiftBalance(_shiftCashBalance ?? 0m);
        _viewModel.SideMenu.ShiftBalanceText = _session.IsShiftOpen
            ? ShiftBalanceHelper.FormatBalance(_shiftCashBalance)
            : "Р РЋР СР ВµР Р…Р В° Р Р…Р Вµ Р С•РЎвЂљР С”РЎР‚РЎвЂ№РЎвЂљР В°";

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