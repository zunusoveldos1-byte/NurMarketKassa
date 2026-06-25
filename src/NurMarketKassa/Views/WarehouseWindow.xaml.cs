using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Models;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.Views;

public partial class WarehouseWindow : Window
{
    private readonly IBarcodeInputService _barcodeInputService;
    private readonly WarehouseViewModel _viewModel;

    public string? InitialBarcode { get; set; }

    public WarehouseWindow(
        WarehouseViewModel viewModel,
        IBarcodeInputService barcodeInputService)
    {
        _viewModel = viewModel;
        _barcodeInputService = barcodeInputService;

        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.RevisionLineAdded += OnRevisionLineAdded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _barcodeInputService.BarcodeScanned += OnBarcodeScanned;

        try
        {
            await _viewModel.EnsureCatalogLoadedAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(InitialBarcode))
                _viewModel.HandleBarcodeScan(InitialBarcode.Trim(), isRevisionTab: true);
        }
        catch (Exception ex)
        {
            PosMessageBox.Show(this, $"Не удалось загрузить каталог: {ex.Message}", "Склад",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void FocusWithBarcode(string? barcode)
    {
        if (!string.IsNullOrWhiteSpace(barcode))
            _viewModel.HandleBarcodeScan(barcode.Trim(), isRevisionTab: true);

        Activate();
        Focus();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _barcodeInputService.BarcodeScanned -= OnBarcodeScanned;
        _viewModel.RevisionLineAdded -= OnRevisionLineAdded;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) =>
        _barcodeInputService.ProcessKeyDown(e);

    private void OnBarcodeScanned(string barcode)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            var isRevisionTab = WarehouseTabs.SelectedIndex == 0;
            _viewModel.HandleBarcodeScan(barcode, isRevisionTab);

            if (!isRevisionTab)
                WriteOffQuantityBox?.Focus();
        });
    }

    private void OnRevisionLineAdded(RevisionLineVm line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnRevisionLineAdded(line));
            return;
        }

        RevisionGrid.SelectedItem = line;
        RevisionGrid.ScrollIntoView(line);

        if (RevisionGrid.Columns.Count > 3)
        {
            RevisionGrid.CurrentCell = new DataGridCellInfo(line, RevisionGrid.Columns[3]);
            RevisionGrid.BeginEdit();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
