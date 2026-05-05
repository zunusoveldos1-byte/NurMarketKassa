using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

#nullable enable
namespace NurMarketKassa.Models.Pos;

public sealed class CatalogProductTileVm : INotifyPropertyChanged
{
    private string? _barcode;
    private string? _stockInfo;
    private bool _isFavorite;
    private double _quantity;
    private ImageSource? _thumb;

    public CatalogProductTileVm(
      string id,
      string title,
      string priceLine,
      bool mustWeigh,
      string? imageUrl = null)
    {
        this.Id = id;
        this.Title = title;
        this.PriceLine = priceLine;
        this.MustWeigh = mustWeigh;
        this.ImageUrl = imageUrl;
    }

    public string? Category { get; set; }

    public string? Brand { get; set; }

    public string Id { get; }

    public string Title { get; }

    public string PriceLine { get; }

    public bool MustWeigh { get; }

    public string? ImageUrl { get; }

    public string? Barcode
    {
        get => this._barcode;
        set
        {
            if (this._barcode == value)
                return;
            this._barcode = value;
            this.OnPropertyChanged(nameof(Barcode));
        }
    }

    public string? StockInfo
    {
        get => this._stockInfo;
        set
        {
            this._stockInfo = value;
            this.OnPropertyChanged(nameof(StockInfo));
        }
    }

    public bool IsFavorite
    {
        get => this._isFavorite;
        set
        {
            this._isFavorite = value;
            this.OnPropertyChanged(nameof(IsFavorite));
        }
    }

    public double Quantity
    {
        get => this._quantity;
        set
        {
            this._quantity = value;
            this.OnPropertyChanged(nameof(Quantity));
        }
    }

    public ImageSource? Thumb
    {
        get => this._thumb;
        set
        {
            if (this._thumb == value)
                return;
            this._thumb = value;
            this.OnPropertyChanged(nameof(Thumb));
        }
    }

    public double PurchasePrice { get; set; }

    public string? Unit { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
