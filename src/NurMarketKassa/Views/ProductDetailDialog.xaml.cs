using System;
using System.Windows;
using NurMarketKassa.ViewModels;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class ProductDetailWindow : Window
    {
        public ProductDetailVm Product { get; }

        public ProductDetailWindow(ProductDetailVm product)
        {
            InitializeComponent();
            Product = product ?? GetFallbackProduct();
            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static ProductDetailVm GetFallbackProduct()
        {
            return new ProductDetailVm
            {
                Id = "demo",
                ProductName = "Картошка23",
                Barcode = "123456789",
                Article = "ART-001",
                Code = "0001",
                CreatedAt = new DateTime(2025, 5, 1, 12, 0, 0),
                Category = "Овощи",
                Country = "Кыргызстан",
                ExpiryDate = new DateTime(2025, 12, 31),
                Group = "Продовольственные",
                Description = "Свежая картошка, фасовка 1 кг",
                Price = 45.00M,
                PurchasePrice = 30.00M,
                MarkupPercent = 0.50M
            };
        }
    }
}