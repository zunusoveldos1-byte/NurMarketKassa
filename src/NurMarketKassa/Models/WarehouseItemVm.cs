using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NurMarketKassa.Models;

public sealed class WarehouseItemVm : INotifyPropertyChanged
{
    private string _id = "";
    private string _productName = "";
    private string _code = "";
    private string _article = "";
    private string _unit = "";
    private decimal _price;
    private decimal _discount;
    private double _stockQuantity;
    private Brush _stockBrush = Brushes.White;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string ProductName { get => _productName; set { _productName = value; OnPropertyChanged(); } }
    public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
    public string Article { get => _article; set { _article = value; OnPropertyChanged(); } }
    public string Unit { get => _unit; set { _unit = value; OnPropertyChanged(); } }
    public decimal Price { get => _price; set { _price = value; OnPropertyChanged(); } }
    public decimal Discount { get => _discount; set { _discount = value; OnPropertyChanged(); } }
    public double StockQuantity { get => _stockQuantity; set { _stockQuantity = value; OnPropertyChanged(); } }
    public Brush StockBrush { get => _stockBrush; set { _stockBrush = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
