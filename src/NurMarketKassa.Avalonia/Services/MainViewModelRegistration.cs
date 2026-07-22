using System.Globalization;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels;
using NurMarketKassa.ViewModels.Main;

namespace NurMarketKassa.AvaloniaHost.Services;

/// <summary>
/// Этот файл собирает и регистрирует ViewModel главного окна кассира,
/// связывая каталог, корзину, панель инструментов и обработчики действий UI.
/// </summary>
internal static class MainViewModelRegistration
{
    public static void AddMainWindowViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainWindowHostBridge>();
        services.AddTransient<MainStatusViewModel>();

        services.AddTransient<MainWindowViewModel>(sp =>
        {
            var bridge = sp.GetRequiredService<MainWindowHostBridge>();
            var session = sp.GetRequiredService<IAppSession>();
            var status = sp.GetRequiredService<MainStatusViewModel>();

            var basket = new BasketPanelViewModel(
                sp.GetRequiredService<ICartService>(),
                sp.GetRequiredService<IUserPrompts>(),
                sp.GetRequiredService<IPosCheckoutService>(),
                sp.GetRequiredService<IDeferredCartService>(),
                sp.GetRequiredService<ICustomerDisplayService>(),
                sp.GetRequiredService<IWindowService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IDispatcher>(),
                LookupCatalogProduct,
                sp.GetService<IPosCheckoutUiFlow>(),
                product => bridge.AddProductFromCatalog?.Invoke(product) ?? Task.CompletedTask,
                () => bridge.OpenDeferredCarts?.Invoke() ?? Task.CompletedTask,
                () => bridge.ApplyOrderDiscount?.Invoke() ?? Task.CompletedTask);

            var catalog = new CatalogPanelViewModel(
                sp.GetRequiredService<ICatalogCacheService>(),
                sp.GetRequiredService<IDispatcher>(),
                sp.GetRequiredService<IConnectivityService>(),
                product => bridge.TryAddProduct(product));

            MainWindowViewModel? main = null;

            var toolbar = new MainToolbarViewModel(
                session,
                status,
                toggleSideMenu: () => main?.ToggleSideMenu(),
                toggleTheme: () => bridge.Window?.ToggleTheme(),
                toggleKeyboard: () => bridge.Window?.ToggleKeyboard(),
                openShiftHandler: () => bridge.Window?.OpenShiftAsync() ?? Task.CompletedTask,
                closeShiftHandler: () => bridge.Window?.CloseShiftAsync() ?? Task.CompletedTask);

            var sideMenu = new SideMenuViewModel(
                session,
                closeMenu: () => main?.CloseSideMenu(),
                navigateWarehouse: () => bridge.Window?.NavigateWarehouse(),
                navigateShifts: () => bridge.Window?.NavigateShifts(),
                navigateReturn: () => bridge.Window?.NavigateReturn(),
                navigateCashOperations: () => bridge.Window?.NavigateCashOperations(),
                navigateFinance: () => bridge.Window?.NavigateFinance(),
                navigateSales: () => bridge.Window?.NavigateSales(),
                navigateSettings: () => bridge.Window?.NavigateSettings(),
                logout: () => bridge.Window?.LogoutAsync());

            main = new MainWindowViewModel(toolbar, catalog, basket, sideMenu, session);
            return main;
        });
    }

    private static CatalogProductTileVm? LookupCatalogProduct(string code)
    {
        var repo = LocalProductRepository.Instance;
        return repo.TryGetTileByBarcode(code) ?? repo.TryGetTileBySku(code);
    }
}
