using System.Text.Json;
using System.Windows;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class ProductDetailDialog : Window
{
    public ProductDetailDialog(CatalogProductTileVm product)
    {
        InitializeComponent();
        DataContext = product;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CatalogProductTileVm vm)
            return;

        try
        {
            var apiBase = App.Settings.ApiBaseUrl;
            string? url = vm.ImageUrl;
            if (string.IsNullOrEmpty(url))
            {
                var det = await App.Api.ProductsDetailAsync(vm.Id).ConfigureAwait(true);
                if (det is { ValueKind: JsonValueKind.Object } el)
                    url = ProductImageUrl.TryGet(el, apiBase);
            }

            LoadingText.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(url))
            {
                NoPhotoText.Visibility = Visibility.Visible;
                return;
            }

            var svc = new ProductThumbService();
            await svc.SetThumbAsync(Dispatcher, App.Api, apiBase, url, vm, default).ConfigureAwait(true);
            if (vm.Thumb == null)
                NoPhotoText.Visibility = Visibility.Visible;
        }
        catch
        {
            LoadingText.Visibility = Visibility.Collapsed;
            NoPhotoText.Visibility = Visibility.Visible;
        }
    }
}
