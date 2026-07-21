using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels.Main;

namespace NurMarketKassa.AvaloniaHost.Services;

internal static class MainViewModelRegistration
{
    public static void AddMainWindowViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainWindowHostBridge>();
        services.AddTransient<MainStatusViewModel>();
        services.AddTransient<CatalogPanelViewModel>();
        services.AddTransient<BasketPanelViewModel>();

        services.AddTransient<MainWindowViewModel>(sp =>
        {
            var bridge = sp.GetRequiredService<MainWindowHostBridge>();
            var session = sp.GetRequiredService<IAppSession>();
            var status = sp.GetRequiredService<MainStatusViewModel>();
            var catalog = sp.GetRequiredService<CatalogPanelViewModel>();
            var basket = sp.GetRequiredService<BasketPanelViewModel>();

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
                navigateFinance: () => bridge.Window?.NavigateFinance(),
                navigateSales: () => bridge.Window?.NavigateSales(),
                navigateSettings: () => bridge.Window?.NavigateSettings(),
                logout: () => bridge.Window?.LogoutAsync());

            main = new MainWindowViewModel(toolbar, catalog, basket, sideMenu, session);
            return main;
        });
    }
}
