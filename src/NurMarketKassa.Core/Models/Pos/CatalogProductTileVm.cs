using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable enable

namespace NurMarketKassa.Models.Pos;

/// <summary>
/// Catalog product tile view-model (UI-agnostic). Image paths are resolved by host converters.
/// </summary>
public sealed class CatalogProductTileVm : INotifyPropertyChanged
{
    private string? _barcode;
    private string? _stockInfo;
    private bool _isFavorite;
    private bool _isUnitInvalid;
    private bool _isLowStock;
    private double _quantity;
    private string? _productImagePath;

    public CatalogProductTileVm(
        string id,
        string title,
        string priceLine,
        bool mustWeigh,
        string? imageUrl = null)
    {
        Id = id;
        Title = title;
        PriceLine = priceLine;
        MustWeigh = mustWeigh;
        ImageUrl = imageUrl;
        OnPropertyChanged(nameof(MustWeigh));
    }

    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string Id { get; }
    public string Title { get; }
    public string PriceLine { get; }
    public bool MustWeigh { get; set; }

    /// <summary>Remote catalog image URL (API).</summary>
    public string? ImageUrl { get; }

    /// <summary>
    /// Local file path or embedded asset name for UI binding (via <c>AssetPathToBitmapConverter</c>).
    /// </summary>
    public string? ProductImagePath
    {
        get => _productImagePath;
        set
        {
            if (_productImagePath == value)
                return;
            _productImagePath = value;
            OnPropertyChanged(nameof(ProductImagePath));
        }
    }

    public string? StatusDisplay { get; set; }
    public string? HotkeyGroupName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ClientName { get; set; }
    public string? Status { get; set; }
    public string? HotkeyGroup { get; set; }

    public string? Barcode
    {
        get => _barcode;
        set
        {
            if (_barcode == value)
                return;
            _barcode = value;
            OnPropertyChanged(nameof(Barcode));
        }
    }

    public string? StockInfo
    {
        get => _stockInfo;
        set
        {
            _stockInfo = value;
            OnPropertyChanged(nameof(StockInfo));
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnPropertyChanged(nameof(IsFavorite));
        }
    }

    public bool IsUnitInvalid
    {
        get => _isUnitInvalid;
        set
        {
            if (_isUnitInvalid == value)
                return;
            _isUnitInvalid = value;
            OnPropertyChanged(nameof(IsUnitInvalid));
        }
    }

    public double Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            OnPropertyChanged(nameof(Quantity));
        }
    }

    public bool IsLowStock
    {
        get => _isLowStock;
        set
        {
            if (_isLowStock == value)
                return;
            _isLowStock = value;
            OnPropertyChanged(nameof(IsLowStock));
        }
    }

    public double PurchasePrice { get; set; }
    public string? Unit { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
