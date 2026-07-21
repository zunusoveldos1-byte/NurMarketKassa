using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaWindowService : IWindowService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<object, Window> _openWindows = new();

    public AvaloniaWindowService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<TResult?> ShowDialogAsync<TViewModel, TResult>(TViewModel viewModel)
        where TViewModel : class
    {
        var window = CreateWindow(viewModel);
        window.DataContext = viewModel;
        WireRequestClose(viewModel, window);

        _openWindows[viewModel] = window;

        try
        {
            var owner = GetActiveWindow()
                ?? throw new InvalidOperationException("Cannot show dialog: no active Avalonia window.");

            return await window.ShowDialog<TResult?>(owner);
        }
        finally
        {
            _openWindows.Remove(viewModel);
        }
    }

    public void ShowWindow<TViewModel>(TViewModel viewModel)
        where TViewModel : class
    {
        var window = CreateWindow(viewModel);
        window.DataContext = viewModel;
        WireRequestClose(viewModel, window);
        _openWindows[viewModel] = window;
        window.Closed += (_, _) => _openWindows.Remove(viewModel);

        var owner = GetActiveWindow();
        if (owner is not null)
            window.Show(owner);
        else
            window.Show();
    }

    public void Close(object viewModel, bool? dialogResult = null)
    {
        if (!_openWindows.TryGetValue(viewModel, out var window))
            return;

        if (dialogResult.HasValue)
            window.Close(dialogResult.Value);
        else
            window.Close();
    }

    private Window CreateWindow(object viewModel) =>
        viewModel switch
        {
            CheckoutViewModel => _services.GetRequiredService<CheckoutDialog>(),
            _ => throw new NotSupportedException(
                $"No Avalonia window registered for view model type '{viewModel.GetType().FullName}'.")
        };

    private static void WireRequestClose(object viewModel, Window window)
    {
        if (viewModel is CheckoutViewModel checkout)
            checkout.RequestClose += result => window.Close(result);
    }

    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.MainWindow;
    }
}
